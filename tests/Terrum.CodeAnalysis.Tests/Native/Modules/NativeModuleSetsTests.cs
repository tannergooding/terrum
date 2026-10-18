// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests.Native.Modules;

public sealed class NativeModuleSetsTests
{
    private const string Alpha = "public int32 Alpha() { return 20; }";
    private const string Beta = "public int32 Beta() { return 22; }";
    private const string Consumer = "int32 Main() { return Alpha() + Beta(); }";

    private static byte[] Producer(string source, string export, string identity)
        => Producer(source, [export], identity);

    private static byte[] Producer(string source, string[] exports, string identity)
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source));
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        return NativeModuleCompiler.CreateProducer(((global::System.Func<BoundProgram>)(() => { var __nunitActual0 = compilation.Program; Assert.That(__nunitActual0, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual0!; }))(),
            exports, identity, NativeRuntimePolicy.FailFast);
    }

    private static byte[] Producer(string source, string export, string identity, NativeRuntimePolicy policy)
        => Producer(source, [export], identity, policy);

    private static byte[] Producer(string source, string[] exports, string identity, NativeRuntimePolicy policy)
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source), (BoundRuntimePolicy)policy);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        return NativeModuleCompiler.CreateProducer(((global::System.Func<BoundProgram>)(() => { var __nunitActual1 = compilation.Program; Assert.That(__nunitActual1, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual1!; }))(),
            exports, identity, policy);
    }

    private static byte[] MakeConsumer(params byte[][] producers) => MakeConsumerSource(Consumer, producers);

    private static byte[] MakeConsumerSource(string source, params byte[][] producers)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(source),
            NativeModuleCompiler.CreateReferenceSet(producers));
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        return NativeModuleCompiler.CreateConsumerSet(((global::System.Func<BoundProgram>)(() => { var __nunitActual2 = compilation.Program; Assert.That(__nunitActual2, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual2!; }))(),
            producers, NativeRuntimePolicy.FailFast);
    }

    private static byte[] MakeConsumerSource(string source, NativeRuntimePolicy policy, params byte[][] producers)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(source),
            NativeModuleCompiler.CreateReferenceSet(producers), (BoundRuntimePolicy)policy);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        return NativeModuleCompiler.CreateConsumerSet(((global::System.Func<BoundProgram>)(() => { var __nunitActual3 = compilation.Program; Assert.That(__nunitActual3, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual3!; }))(),
            producers, policy);
    }

    private static byte[] WithUpdatedSetChecksum(byte[] artifact, Action<byte[]> mutate)
    {
        var result = artifact.ToArray();
        mutate(result);
        SHA256.HashData(result.AsSpan(0, result.Length - 32)).CopyTo(result, result.Length - 32);
        return result;
    }

    private static byte[] WithSourceGrantRequirement(byte[] artifact) =>
        WithUpdatedSetChecksum(artifact, bytes => {
            var dependencyCount = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(6, 2));
            var at = 16;
            for (var dependency = 0; dependency < dependencyCount; dependency++)
            {
                var identityLength = bytes[at++];
                at += identityLength;
                var importCount = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(at, 2));
                at += 2;
                for (var import = 0; import < importCount; import++)
                {
                    var importLength = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(at, 2));
                    at += 2 + importLength;
                }
                var machineImportCount = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(at, 2));
                at += 2;
                for (var import = 0; import < machineImportCount; import++)
                {
                    var importLength = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(at, 2));
                    at += 2 + importLength;
                }
            }

            Assert.That(bytes.AsSpan(at, 4).SequenceEqual("TRRM"u8), Is.True);
            var profileLength = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(at + 8, 4));
            var requirementOffset = checked(at + 8 + 4 + profileLength + 2);
            Assert.That(bytes[requirementOffset], Is.EqualTo(0));
            bytes[requirementOffset] = 1;
        });

    private static int UniqueOffset(byte[] bytes, string text)
    {
        var value = Encoding.ASCII.GetBytes(text);
        var offset = bytes.AsSpan().IndexOf(value);
        Assert.That(offset >= 0, Is.True, $"Missing expected serialized value '{text}'.");
        Assert.That(bytes.AsSpan(offset + 1).IndexOf(value), Is.EqualTo(-1));
        return offset;
    }

    [Test]
    public async Task PersistedConsumerRunsIndependentReplacementProvidersAsync()
    {
        var alpha = Producer(Alpha, "Alpha", "First");
        var beta = Producer(Beta, "Beta", "Second");
        var bytes = MakeConsumer(beta, alpha);
        var unchanged = bytes.ToArray();
        NativeModuleCompiler.ValidateConsumerSet(bytes, [alpha, beta]);
        Assert.That(NativeModuleCompiler.InspectConsumerSet(bytes).Dependencies, Is.EqualTo(["First", "Second"]));
        Assert.That(await NativeModuleCompiler.RunConsumerSetAsync(bytes, [beta, alpha],
            TimeSpan.FromSeconds(10), NativeRuntimePolicy.FailFast), Is.EqualTo(42));
        var alphaReplacement = Producer(Alpha.Replace("20", "21", StringComparison.Ordinal), "Alpha", "First");
        var betaReplacement = Producer(Beta.Replace("22", "23", StringComparison.Ordinal), "Beta", "Second");
        Assert.That(await NativeModuleCompiler.RunConsumerSetAsync(bytes, [alphaReplacement, beta],
            TimeSpan.FromSeconds(10), NativeRuntimePolicy.FailFast), Is.EqualTo(43));
        Assert.That(await NativeModuleCompiler.RunConsumerSetAsync(bytes, [betaReplacement, alpha],
            TimeSpan.FromSeconds(10), NativeRuntimePolicy.FailFast), Is.EqualTo(43));
        Assert.That(bytes, Is.EqualTo(unchanged));
    }

    [Test]
    public void DirectNativeImportsRemainSeparateFromModuleSetDependencies()
    {
        var alpha = Producer(Alpha, "Alpha", "First");
        var beta = Producer(Beta, "Beta", "Second");
        var consumer = MakeConsumerSource("""
            [Terrum.Interop.NativeImport("example.dll", "ReadValue")]
            [Terrum.Interop.NativeOutput(0, NonZero)]
            extern int32 ReadValue(write ptr<int32> value);
            [Terrum.Interop.NativeImport("kernel32.dll", "CompareStringOrdinal")]
            [Terrum.Interop.NativeParameter(1, Read, NoEscape, 1)]
            [Terrum.Interop.NativeParameter(3, Read, NoEscape, 1)]
            extern int32 CompareStringOrdinal(ptr<char16> first, int32 firstLength,
                ptr<char16> second, int32 secondLength, int32 ignoreCase);

            int32 Main() {
                mut int32 value = 0;
                char16 first = 'A';
                char16 second = 'A';
                unsafe {
                    if (ReadValue(&value) == 0) { return -1; }
                    if (CompareStringOrdinal(&first, 1, &second, 1, 0) != 2) { return -2; }
                }
                return Alpha() + Beta() + value;
            }
            """, alpha, beta);

        Assert.That(NativeModuleCompiler.InspectConsumerSet(consumer).Dependencies,
            Is.EqualTo(["First", "Second"]));
        NativeModuleCompiler.ValidateConsumerSet(consumer, [beta, alpha]);
    }

    [Test]
    public void PersistedConsumerSetWithoutImportsValidatesAgainstPopulatedProviders()
    {
        var alpha = Producer("""
            [Blittable] [Layout(LayoutKind.Sequential)] public struct Node { public int32 Value; }
            public Node Echo(Node value) { return value; }
            """, "Echo", "First");
        var beta = Producer(Beta, "Beta", "Second");
        var consumer = MakeConsumerSource("int32 Main() { return 42; }", alpha, beta);

        NativeModuleCompiler.ValidateConsumerSet(consumer, [alpha, beta]);
    }

    [Test]
    public void ProviderIdentityOrderControlsReferenceAndPersistedDependencyOrder()
    {
        var firstByIdentity = Producer(Beta, "Beta", "AProvider");
        var secondByIdentity = Producer(Alpha, "Alpha", "ZProvider");
        var providers = new[] { secondByIdentity, firstByIdentity };
        var reference = NativeModuleCompiler.CreateReferenceSet(providers);
        Assert.That(reference.Functions.Select(static function => function.Name), Is.EqualTo(["Beta", "Alpha"]));

        var compilation = Compilation.Create(SyntaxTree.Parse(Consumer), reference);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var consumer = NativeModuleCompiler.CreateConsumerSet(((global::System.Func<BoundProgram>)(() => { var __nunitActual4 = compilation.Program; Assert.That(__nunitActual4, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual4!; }))(),
            providers, NativeRuntimePolicy.FailFast);

        Assert.That(NativeModuleCompiler.InspectConsumerSet(consumer).Dependencies, Is.EqualTo(["AProvider", "ZProvider"]));
        NativeModuleCompiler.ValidateConsumerSet(consumer, providers);
    }

    [Test]
    public void RejectsMissingDuplicateAmbiguousAndIncompatibleDependencies()
    {
        var alpha = Producer(Alpha, "Alpha", "First");
        var beta = Producer(Beta, "Beta", "Second");
        var consumer = MakeConsumer(alpha, beta);
        Assert.That(consumer[4], Is.EqualTo(2));
        var sourceRequired = WithSourceGrantRequirement(consumer);
        _ = Assert.Throws<ModuleFormatException>(() => NativeModuleCompiler.InspectConsumerSet(sourceRequired))!;
        _ = Assert.Throws<ModuleFormatException>(() => NativeModuleCompiler.ValidateConsumerSet(sourceRequired, [alpha, beta]))!;
        _ = Assert.Throws<ModuleContractException>(() => NativeModuleCompiler.ValidateConsumerSet(consumer, [alpha]))!;
        _ = Assert.Throws<ModuleContractException>(() => NativeModuleCompiler.ValidateConsumerSet(consumer, [alpha, alpha]))!;
        _ = Assert.Throws<ModuleContractException>(() => NativeModuleCompiler.CreateReferenceSet([
            alpha, Producer("public int32 Alpha() { return 1; }", "Alpha", "Other")]))!;
        _ = Assert.Throws<ModuleContractException>(() => NativeModuleCompiler.ValidateConsumerSet(consumer, [
            Producer("public int64 Alpha() { return 20; }", "Alpha", "First"), beta]))!;
        _ = Assert.Throws<ModuleContractException>(() => NativeModuleCompiler.ValidateConsumerSet(consumer, [
            alpha, Producer("public int32 Beta() { return 22; }", "Beta", "Third")]))!;
        _ = Assert.Throws<ModuleContractException>(() => NativeModuleCompiler.ValidateConsumerSet(consumer, [
            alpha, beta, Producer("public int32 Gamma() { return 1; }", "Gamma", "Third")]))!;
        _ = Assert.Throws<ModuleContractException>(() => NativeModuleCompiler.ValidateConsumerSet(consumer, [
            alpha, Producer(Beta, "Beta", "second")]))!;
        var checkedCompilation = Compilation.CreateLibrary(SyntaxTree.Parse(Beta),
            BoundRuntimePolicy.CheckedExceptions);
        Assert.That(checkedCompilation.HasErrors, Is.False);
        var checkedBeta = NativeModuleCompiler.CreateProducer(((global::System.Func<BoundProgram>)(() => { var __nunitActual5 = checkedCompilation.Program; Assert.That(__nunitActual5, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual5!; }))(),
            ["Beta"], "Second", NativeRuntimePolicy.CheckedExceptions);
        _ = Assert.Throws<ModuleContractException>(() => NativeModuleCompiler.ValidateConsumerSet(consumer,
            [alpha, checkedBeta]))!;
        var tampered = consumer.ToArray();
        tampered[^1] ^= 1;
        _ = Assert.Throws<ModuleFormatException>(() => NativeModuleCompiler.ValidateConsumerSet(tampered, [alpha, beta]))!;
        var previousVersion = WithUpdatedSetChecksum(consumer, bytes => bytes[4] = 1);
        _ = Assert.Throws<ModuleFormatException>(() => NativeModuleCompiler.InspectConsumerSet(previousVersion))!;
        var version = WithUpdatedSetChecksum(consumer, bytes => bytes[4] = 3);
        _ = Assert.Throws<ModuleFormatException>(() => NativeModuleCompiler.InspectConsumerSet(version))!;
        var mapping = WithUpdatedSetChecksum(consumer, bytes => {
            var firstImportOffset = 16 + 1 + "First".Length + 2 + 2;
            bytes[firstImportOffset] = (byte)'G';
        });
        _ = Assert.Throws<ModuleFormatException>(() => NativeModuleCompiler.ValidateConsumerSet(mapping, [alpha, beta]))!;
        var bounds = WithUpdatedSetChecksum(consumer, bytes =>
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8), int.MaxValue));
        _ = Assert.Throws<ModuleFormatException>(() => NativeModuleCompiler.InspectConsumerSet(bounds))!;
    }
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public void ReplacementChecksRetainedEffectsUnderBothPolicies(NativeRuntimePolicy policy)
    {
        var beta = Producer(Beta, "Beta", "Second", policy);
        var noThrow = Producer("[NoThrow] public int32 Alpha() { return 20; }", "Alpha", "First", policy);
        var effectConsumer = MakeConsumerSource(Consumer, policy, noThrow, beta);
        _ = Assert.Throws<ModuleContractException>(() => NativeModuleCompiler.ValidateConsumerSet(effectConsumer,
            [Producer(Alpha, "Alpha", "First", policy), beta]))!;

        var noReturn = Producer("[DoesNotReturn] public void Stop() { require false; }", "Stop", "First", policy);
        var noReturnConsumer = MakeConsumerSource("void Main() { Stop(); }", policy, noReturn, beta);
        _ = Assert.Throws<ModuleContractException>(() => NativeModuleCompiler.ValidateConsumerSet(noReturnConsumer,
            [Producer("public void Stop() { }", "Stop", "First", policy), beta]))!;
    }

    [Test]
    public void ReplacementChecksRetainedValueLayoutsAndOutputModes()
    {
        var beta = Producer(Beta, "Beta", "Second");

        const string Value = """
            [Blittable] [Layout(LayoutKind.Sequential)] public struct Box {
                public int32 Value;
                public Box(int32 value) { Value = value; }
            }
            public Box Alpha() { return new Box(20); }
            """;
        var boxed = Producer(Value, "Alpha", "First");
        var boxedConsumer = MakeConsumerSource(
            "int32 Main() { Box value = Alpha(); return value.Value + Beta(); }", boxed, beta);
        NativeModuleCompiler.ValidateConsumerSet(boxedConsumer, [boxed, beta]);
        _ = Assert.Throws<ModuleContractException>(() => NativeModuleCompiler.ValidateConsumerSet(boxedConsumer, [
            Producer(Value.Replace("public int32 Value;", "public uint32 Value;", StringComparison.Ordinal)
                .Replace("Box(int32 value)", "Box(uint32 value)", StringComparison.Ordinal)
                .Replace("new Box(20)", "new Box(uint32(20))", StringComparison.Ordinal),
                "Alpha", "First"), beta]))!;

        const string Output = "public bool Alpha(out int32 value) { value = 20; return true; }";
        var output = Producer(Output, "Alpha", "First");
        var outputConsumer = MakeConsumerSource(
            "int32 Main() { require Alpha(out var value); return value + Beta(); }", output, beta);
        _ = Assert.Throws<ModuleContractException>(() => NativeModuleCompiler.ValidateConsumerSet(outputConsumer, [
            Producer("public int32 Alpha() { return 20; }", "Alpha", "First"), beta]))!;

        const string Owner = """
            [Blittable] [Layout(LayoutKind.Sequential)]
            public unique struct Lease {
                public int32 Value;
                public Lease(int32 value) { Value = value; }
                [NoThrow] [Cleanup] public void Close() { }
            }
            public Lease Alpha() { return new Lease(20); }
            """;
        var owned = Producer(Owner, ["Alpha", "Lease.Close"], "First");
        var ownedConsumer = MakeConsumerSource(
            "int32 Main() { Lease value = Alpha(); return value.Value + Beta(); }", owned, beta);
        NativeModuleCompiler.ValidateConsumerSet(ownedConsumer, [owned, beta]);
        Assert.That(NativeModuleCompiler.InspectConsumerSet(ownedConsumer).NativeProfileVersion, Is.Not.EqualTo(Compilation.ProfileVersion));
        _ = Assert.Throws<ModuleContractException>(() => NativeModuleCompiler.ValidateConsumerSet(ownedConsumer, [
            Producer(Owner.Replace("public int32 Value;", "public int32 Identifier;", StringComparison.Ordinal)
                .Replace("Value = value;", "Identifier = value;", StringComparison.Ordinal),
                ["Alpha", "Lease.Close"], "First"), beta]))!;

        var unusedOwner = Producer(Alpha + """
             unique struct Unused {
                 public int32 Value;
                 public Unused(int32 value) { Value = value; }
                 [NoThrow] [Cleanup] public void Close() { }
             }
             """, "Alpha", "First");
        var unownedConsumer = MakeConsumer(unusedOwner, beta);
        Assert.That(NativeModuleCompiler.InspectConsumerSet(unownedConsumer).NativeProfileVersion, Is.EqualTo(Compilation.ProfileVersion));
    }

    [Test]
    public async Task CheckedExceptionPolicyBindsBothProvidersAsync()
    {
        static byte[] CheckedProducer(string source, string name, string identity)
        {
            var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source),
                BoundRuntimePolicy.CheckedExceptions);
            Assert.That(compilation.HasErrors, Is.False);
            return NativeModuleCompiler.CreateProducer(((global::System.Func<BoundProgram>)(() => { var __nunitActual6 = compilation.Program; Assert.That(__nunitActual6, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual6!; }))(),
                [name], identity, NativeRuntimePolicy.CheckedExceptions);
        }

        var first = CheckedProducer(Alpha, "Alpha", "First");
        var second = CheckedProducer(Beta, "Beta", "Second");
        var sources = Compilation.Create(SyntaxTree.Parse(Consumer),
            NativeModuleCompiler.CreateReferenceSet([first, second]), BoundRuntimePolicy.CheckedExceptions);
        Assert.That(sources.HasErrors, Is.False);
        var consumer = NativeModuleCompiler.CreateConsumerSet(((global::System.Func<BoundProgram>)(() => { var __nunitActual7 = sources.Program; Assert.That(__nunitActual7, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual7!; }))(),
            [second, first], NativeRuntimePolicy.CheckedExceptions);
        Assert.That(await NativeModuleCompiler.RunConsumerSetAsync(consumer, [first, second],
            TimeSpan.FromSeconds(10), NativeRuntimePolicy.CheckedExceptions), Is.EqualTo(42));
        _ = Assert.Throws<ModuleContractException>(() => NativeModuleCompiler.ValidateConsumerSet(consumer,
            [Producer(Alpha, "Alpha", "First"), second]))!;
    }
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public async Task ConditionalOutOutputRemainsVacantAcrossNativeConsumerSetsAsync(NativeRuntimePolicy policy)
    {
        const string OutputProvider = """
            public bool TryPublish(bool publish, out int32 result) {
                result = publish ? 42 : 41;
                return publish;
            }
            public void Complete(init ref<int32> result) { result = 43; }
            """;
        var outputProvider = Producer(OutputProvider, ["TryPublish", "Complete"], "First", policy);
        var second = Producer(Beta, "Beta", "Second", policy);
        const string ConsumerSource = """
            int32 Main() {
                if (TryPublish(false, out var failed)) { return 1; }
                Complete(init failed);
                if (failed != 43) { return 2; }
                if (!TryPublish(true, out var published)) { return 3; }
                return published + Beta();
            }
            """;
        var references = NativeModuleCompiler.CreateReferenceSet([second, outputProvider]);
        var compilation = Compilation.Create(SyntaxTree.Parse(ConsumerSource), references,
            (BoundRuntimePolicy)policy);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var consumer = NativeModuleCompiler.CreateConsumerSet(((global::System.Func<BoundProgram>)(() => { var __nunitActual8 = compilation.Program; Assert.That(__nunitActual8, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual8!; }))(),
            [outputProvider, second], policy);
        var originalConsumer = (byte[])consumer.Clone();

        Assert.That(await NativeModuleCompiler.RunConsumerSetAsync(consumer, [second, outputProvider],
            TimeSpan.FromSeconds(10), policy).ConfigureAwait(true), Is.EqualTo(64));

        var replacementSource = OutputProvider.Replace("publish ? 42 : 41", "publish ? 45 : 0",
            StringComparison.Ordinal);
        var replacement = Producer(replacementSource, ["TryPublish", "Complete"], "First", policy);
        NativeModuleCompiler.ValidateConsumerSet(consumer, [replacement, second]);
        Assert.That(await NativeModuleCompiler.RunConsumerSetAsync(consumer, [replacement, second],
            TimeSpan.FromSeconds(10), policy).ConfigureAwait(true), Is.EqualTo(67));
        Assert.That(consumer, Is.EqualTo(originalConsumer));
    }
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public async Task InitRefOutputBindsAcrossNativeConsumerSetsAsync(NativeRuntimePolicy policy)
    {
        var initializerCompilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            public int32 Initialize(int32 value, init ref<int32> result) {
                result = value;
                return 0;
            }
            """), (BoundRuntimePolicy)policy);
        Assert.That(initializerCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, initializerCompilation.Diagnostics));
        var initializer = NativeModuleCompiler.CreateProducer(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual9 = initializerCompilation.Program; Assert.That(__nunitActual9, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual9!; }))(), ["Initialize"], "Initializer", policy);

        var secondCompilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            "public int32 Beta() { return 22; }"), (BoundRuntimePolicy)policy);
        Assert.That(secondCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, secondCompilation.Diagnostics));
        var second = NativeModuleCompiler.CreateProducer(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual10 = secondCompilation.Program; Assert.That(__nunitActual10, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual10!; }))(), ["Beta"], "Second", policy);

        const string ConsumerSource = """
            int32 Main() {
                init int32 result;
                int32 status = Initialize(result: init result, value: 20);
                return result + Beta() + status;
            }
            """;
        var reference = NativeModuleCompiler.CreateReferenceSet([second, initializer]);
        var consumerCompilation = Compilation.Create(SyntaxTree.Parse(ConsumerSource), reference,
            (BoundRuntimePolicy)policy);
        Assert.That(consumerCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
        var consumer = NativeModuleCompiler.CreateConsumerSet(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual11 = consumerCompilation.Program; Assert.That(__nunitActual11, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual11!; }))(), [initializer, second], policy);
        NativeModuleCompiler.ValidateConsumerSet(consumer, [second, initializer]);
        Assert.That(await NativeModuleCompiler.RunConsumerSetAsync(consumer, [second, initializer],
            TimeSpan.FromSeconds(10), policy).ConfigureAwait(true), Is.EqualTo(42));
    }
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public async Task FloatingOutputsBindAcrossNativeConsumerSetsAsync(NativeRuntimePolicy policy)
    {
        var providerCompilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            public bool TryGet(out float32 value) {
                value = 3.5;
                return true;
            }
            public bool Initialize(float64 value, init ref<float64> result) {
                result = value;
                return true;
            }
            """), (BoundRuntimePolicy)policy);
        Assert.That(providerCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, providerCompilation.Diagnostics));
        var provider = NativeModuleCompiler.CreateProducer(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual12 = providerCompilation.Program; Assert.That(__nunitActual12, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual12!; }))(),
            ["TryGet", "Initialize"], "Provider", policy);
        var secondCompilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            "public int32 Beta() { return 0; }"), (BoundRuntimePolicy)policy);
        Assert.That(secondCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, secondCompilation.Diagnostics));
        var second = NativeModuleCompiler.CreateProducer(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual13 = secondCompilation.Program; Assert.That(__nunitActual13, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual13!; }))(), ["Beta"], "Second", policy);

        const string ConsumerSource = """
            int32 Main() {
                init float64 initialized;
                if (!Initialize(7.75, init initialized)) { return 1; }
                if (!TryGet(out var output)) { return 2; }
                return initialized == 7.75 && output == 3.5 ? 42 + Beta() : 0;
            }
            """;
        var references = NativeModuleCompiler.CreateReferenceSet([provider, second]);
        var consumerCompilation = Compilation.Create(SyntaxTree.Parse(ConsumerSource), references,
            (BoundRuntimePolicy)policy);
        Assert.That(consumerCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
        var consumer = NativeModuleCompiler.CreateConsumerSet(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual14 = consumerCompilation.Program; Assert.That(__nunitActual14, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual14!; }))(), [provider, second], policy);

        Assert.That(await NativeModuleCompiler.RunConsumerSetAsync(consumer, [provider, second],
            TimeSpan.FromSeconds(10), policy).ConfigureAwait(true), Is.EqualTo(42));
    }

    [Test]
    public async Task InitRefOutputRemainsVacantAfterProviderFailureInNativeConsumerSetAsync()
    {
        const NativeRuntimePolicy policy = NativeRuntimePolicy.CheckedExceptions;
        var initializerCompilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            public void FailAfterWrite(int32 value, init ref<int32> result) {
                result = value;
                require false;
            }
            public void Fill(int32 value, init ref<int32> result) {
                result = value;
            }
            """), BoundRuntimePolicy.CheckedExceptions);
        Assert.That(initializerCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, initializerCompilation.Diagnostics));
        var initializer = NativeModuleCompiler.CreateProducer(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual15 = initializerCompilation.Program; Assert.That(__nunitActual15, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual15!; }))(),
            ["FailAfterWrite", "Fill"], "Initializer", policy);

        var secondCompilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            "public int32 Beta() { return 22; }"), BoundRuntimePolicy.CheckedExceptions);
        Assert.That(secondCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, secondCompilation.Diagnostics));
        var second = NativeModuleCompiler.CreateProducer(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual16 = secondCompilation.Program; Assert.That(__nunitActual16, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual16!; }))(), ["Beta"], "Second", policy);

        const string ConsumerSource = """
            int32 Main() {
                init int32 result;
                unsafe {
                    try {
                        FailAfterWrite(41, init result);
                        return 1;
                    } catch { }
                }
                Fill(20, init result);
                return result + Beta();
            }
            """;
        var reference = NativeModuleCompiler.CreateReferenceSet([initializer, second]);
        var consumerCompilation = Compilation.Create(SyntaxTree.Parse(ConsumerSource), reference,
            BoundRuntimePolicy.CheckedExceptions);
        Assert.That(consumerCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
        var consumer = NativeModuleCompiler.CreateConsumerSet(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual17 = consumerCompilation.Program; Assert.That(__nunitActual17, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual17!; }))(), [initializer, second], policy);
        NativeModuleCompiler.ValidateConsumerSet(consumer, [second, initializer]);
        Assert.That(await NativeModuleCompiler.RunConsumerSetAsync(consumer, [second, initializer],
            TimeSpan.FromSeconds(10), policy).ConfigureAwait(true), Is.EqualTo(42));
    }
    [TestCase("int64","22")]
    [TestCase("uint32","22")]
    public async Task BothIndependentNestedValueTablesBindAndExecuteAsync(string secondType, string secondValue)
    {
        const string First = """
            [Blittable] [Layout(LayoutKind.Sequential)] public struct FirstInner {
                public int32 Value;
                public FirstInner(int32 value) { Value = value; }
            }
            [Blittable] [Layout(LayoutKind.Sequential)] public struct FirstOuter {
                public FirstInner Child;
                public FirstOuter(FirstInner child) { Child = child; }
            }
            public FirstOuter Alpha() { return new FirstOuter(new FirstInner(20)); }
            """;
        var second = $$"""
            [Blittable] [Layout(LayoutKind.Sequential)] public struct SecondInner {
                public {{secondType}} Value;
                public SecondInner({{secondType}} value) { Value = value; }
            }
            [Blittable] [Layout(LayoutKind.Sequential)] public struct SecondOuter {
                public SecondInner Child;
                public SecondOuter(SecondInner child) { Child = child; }
            }
            public SecondOuter Beta() { return new SecondOuter(new SecondInner({{secondValue}})); }
            """;
        var first = Producer(First, "Alpha", "First");
        var provider = Producer(second, "Beta", "Second");
        var reference = NativeModuleCompiler.CreateReferenceSet([provider, first]);
        foreach (var (outer, inner) in new[] { ("FirstOuter", "FirstInner"), ("SecondOuter", "SecondInner") })
        {
            var structure = global::System.Linq.Enumerable.Single(reference.Structures, item => item.Name == outer);
            Assert.That(reference.Structures[(int)structure.Fields[0].Type - 100].Name, Is.EqualTo(inner));
        }
        const string Source = """
            struct Unused { public int64 Marker; }
            int32 Main() {
                FirstOuter first = Alpha();
                SecondOuter second = Beta();
                return first.Child.Value + int32(second.Child.Value);
            }
            """;
        var executable = MakeConsumerSource(Source, provider, first);
        Assert.That(await NativeModuleCompiler.RunConsumerSetAsync(executable, [first, provider],
            TimeSpan.FromSeconds(10), NativeRuntimePolicy.FailFast), Is.EqualTo(42));
    }

    [Test]
    public void PublicIrCannotAddConsumerOwnedMemberToImportedType()
    {
        const string Provider = """
            [Blittable] [Layout(LayoutKind.Sequential)] public struct Box { public int32 Value; public Box(int32 value) { Value = value; } }
            public Box Alpha() { return new Box(20); }
            """;
        var first = Producer(Provider, "Alpha", "First");
        var second = Producer(Beta, "Beta", "Second");
        var reference = NativeModuleCompiler.CreateReferenceSet([first, second]);
        var result = Compilation.Create(SyntaxTree.Parse(
            "int32 Main() { Box value = Alpha(); return value.Value + Beta(); }"), reference);
        Assert.That(result.HasErrors, Is.False);
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual18 = result.Program; Assert.That(__nunitActual18, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual18!; }))();
        var probe = new BoundFunction("Box.Probe", PrimitiveType.Void, [], 0,
            new BoundBlock([new BoundReturn(null, default)], default), default) {
            DeclaringStruct = "Box",
            IsStatic = true,
        };
        var forged = program with { Functions = program.Functions.Add(probe) };
        var exception = Assert.Throws<ModuleContractException>(() => NativeModuleCompiler.CreateConsumerSet(forged,
            [first, second], NativeRuntimePolicy.FailFast))!;
        Assert.That(exception.Message, Is.EqualTo("Consumer cannot define members of an imported value type."));
    }

    [Test]
    public void RetainedImportOwnershipCannotBeLoweredByRehashingSet()
    {
        const string Owner = """
            [Blittable] [Layout(LayoutKind.Sequential)]
            public unique struct Token {
                public int32 Value;
                public Token(int32 value) { Value = value; }
                [NoThrow] [Cleanup] public void Close() { }
            }
            public Token New() { return new Token(20); }
            public int32 Take(Token value) { return value.Value; }
            """;
        var first = Producer(Owner, ["New", "Take", "Token.Close"], "First");
        var second = Producer(Beta, "Beta", "Second");
        var consumer = MakeConsumerSource(
            "int32 Main() { Token token = New(); return Take(move token) + Beta(); }", first, second);
        Assert.That(NativeModuleCompiler.InspectConsumerSet(consumer).NativeProfileVersion, Is.EqualTo("native-13"));
        var tampered = WithUpdatedSetChecksum(consumer, bytes => {
            var profile = UniqueOffset(bytes, "native-13");
            bytes[profile + "native-1".Length] = (byte)'2';
            var contract = UniqueOffset(bytes, "Take|");
            var end = bytes.AsSpan(contract).IndexOf(Encoding.ASCII.GetBytes("|MayReturn"));
            Assert.That(end >= 0, Is.True);
            var marker = contract + end + "|MayReturn".Length;
            Assert.That(bytes[marker], Is.EqualTo((byte)1));
            Assert.That(bytes[marker + 1], Is.EqualTo((byte)0));
            Assert.That(bytes[marker + 2], Is.EqualTo((byte)1));
            bytes[marker + 2] = 0;
        });
        _ = Assert.Throws<ModuleContractException>(() => NativeModuleCompiler.ValidateConsumerSet(tampered,
            [first, second]))!;
    }
}
