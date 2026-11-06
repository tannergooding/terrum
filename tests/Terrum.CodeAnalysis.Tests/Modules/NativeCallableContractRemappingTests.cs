using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using NUnit.Framework;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Terrum.CodeAnalysis.Tests.Semantics;

namespace Terrum.CodeAnalysis.Tests.Modules;

[TestFixture]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "NUnit instantiates this fixture through reflection.")]
internal sealed class NativeCallableContractRemappingTests
{
    [Test]
    public void NativeCallableAddressContractsRemapAcrossPrivateSetAndGraphProviders()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        var original = CreateProvider(reverse: false, changeIdentity: false);
        var reordered = CreateProvider(reverse: true, changeIdentity: false);
        var changed = CreateProvider(reverse: true, changeIdentity: true);
        var originalReference = ModuleCompiler.CreateReference(original.Private);
        var reorderedReference = ModuleCompiler.CreateReference(reordered.Private);
        var originalIdentityType = originalReference.Functions.Single(
            static function => function.Name == "Identity")
            .NativeFunctionPointerType;
        var reorderedIdentityType = reorderedReference.Functions.Single(
            static function => function.Name == "Identity")
            .NativeFunctionPointerType;
        Assert.That(originalIdentityType, Is.Not.EqualTo(reorderedIdentityType));

        var consumerSource = SyntaxTree.Parse("""
            int32 Main() {
                unsafe {
                    [Terrum.Interop.NativeCallConv(Terrum.Interop.NativeCallingConvention.Unmanaged)]
                    fnptr<int32, int32> callback = &Identity;
                    return callback(42);
                }
            }
            """);
        var privateCompilation = CreateConsumer(
            consumerSource, originalReference);
        Assert.That(privateCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, privateCompilation.Diagnostics));
        var privateConsumer = ModuleCompiler.CreateConsumer(
            privateCompilation.Program!, original.Private);
        Assert.DoesNotThrow(() =>
            ModuleCompiler.Link(privateConsumer, reordered.Private));
        _ = Assert.Throws<ModuleContractException>(() =>
            ModuleCompiler.Link(privateConsumer, changed.Private));

        var extra = CreateExtraProvider();
        var providers = new[] { original.Native, extra };
        var reorderedProviders = new[] { reordered.Native, extra };
        var changedProviders = new[] { changed.Native, extra };
        var setCompilation = CreateConsumer(consumerSource,
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

        var graphCompilation = CreateConsumer(consumerSource,
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
    public void PrivateModuleRejectsOutOfRangeNativeCallableAddressContractReference()
    {
        var provider = CreateProvider(reverse: false, changeIdentity: false);
        var malformed = provider.Private.ToArray();
        var marker = Encoding.ASCII.GetBytes(
            "|NativeAddressContract:fnptr#");
        var markerOffset = FindBytes(malformed, marker);
        malformed[markerOffset + marker.Length] = (byte)'9';

        _ = Assert.Throws<ModuleFormatException>(() =>
            ModuleCompiler.CreateReference(malformed));
    }

    [Test]
    public void PrivateModuleRejectsDuplicateAndMisorderedCallableTokens()
    {
        var producer = CreateCallableEntryProducer();
        var callableToken = Encoding.ASCII.GetBytes(
            "NativeCallable:Unmanaged");
        var entryToken = Encoding.ASCII.GetBytes(
            "NativeCallableEntry:4142");
        Assert.That(callableToken, Has.Length.EqualTo(entryToken.Length));

        var duplicate = producer.ToArray();
        var entryOffset = FindBytes(duplicate, entryToken);
        Array.Copy(callableToken, 0, duplicate, entryOffset,
            callableToken.Length);
        _ = Assert.Throws<ModuleFormatException>(() =>
            ModuleCompiler.CreateReference(duplicate));

        var misordered = producer.ToArray();
        var callableOffset = FindBytes(misordered, callableToken);
        entryOffset = FindBytes(misordered, entryToken);
        Array.Copy(entryToken, 0, misordered, callableOffset,
            entryToken.Length);
        Array.Copy(callableToken, 0, misordered, entryOffset,
            callableToken.Length);
        _ = Assert.Throws<ModuleFormatException>(() =>
            ModuleCompiler.CreateReference(misordered));
    }

    [Test]
    public void PrivateModuleRejectsMalformedAndInconsistentCallableEntryTokens()
    {
        var producer = CreateCallableEntryProducer();
        var entryToken = Encoding.ASCII.GetBytes(
            "NativeCallableEntry:4142");
        var entryOffset = FindBytes(producer, entryToken);

        var malformed = producer.ToArray();
        malformed[entryOffset + entryToken.Length - 1] = (byte)'Z';
        _ = Assert.Throws<ModuleFormatException>(() =>
            ModuleCompiler.CreateReference(malformed));

        var inconsistent = producer.ToArray();
        var otherEntry = Encoding.ASCII.GetBytes(
            "NativeCallableEntry:4344");
        Assert.That(otherEntry, Has.Length.EqualTo(entryToken.Length));
        Array.Copy(otherEntry, 0, inconsistent, entryOffset,
            otherEntry.Length);
        _ = Assert.Throws<ModuleFormatException>(() =>
            ModuleCompiler.CreateReference(inconsistent));
    }

    [Test]
    public void PrivateBodylessNativeCallableRequiresItsAddressContractRoot()
    {
        var provider = CreateProvider(reverse: false, changeIdentity: false);
        var reference = ModuleCompiler.CreateReference(provider.Private);
        var compilation = CreateConsumer(SyntaxTree.Parse("""
            int32 Main() {
                unsafe {
                    [Terrum.Interop.NativeCallConv(Terrum.Interop.NativeCallingConvention.Unmanaged)]
                    fnptr<int32, int32> callback = &Identity;
                    return callback(42);
                }
            }
            """), reference);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(
            compilation.Program!, provider.Private);
        var malformed = consumer.ToArray();
        var marker = Encoding.ASCII.GetBytes(
            "NativeAddressContract:fnptr#");
        var offset = FindBytes(malformed, marker);
        malformed[offset] = (byte)'n';

        _ = Assert.Throws<ModuleFormatException>(() =>
            ModuleCompiler.Link(malformed, provider.Private));
    }

    [Test]
    public void PrivateNativeCallableRootPreservesDeclaredCallerUnsafety()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Terrum.Interop.NativeCallable]
            public unsafe int32 DeclaredUnsafe(int32 value) { return value; }

            [Terrum.Interop.NativeCallable]
            public int32 RoleUnsafe(int32 value) { return value; }
            """), NativeCoreRegistrationTests.CreateRegisteredCompilationOptions());
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var producer = ModuleCompiler.CreateProducer(compilation.Program!,
            ["DeclaredUnsafe", "RoleUnsafe"]);

        var reference = ModuleCompiler.CreateReference(producer);
        foreach (var (name, declaredUnsafe, expectedMask) in new[] {
            ("DeclaredUnsafe", true, 4u),
            ("RoleUnsafe", false, 0u),
        })
        {
            var function = reference.Functions.Single(candidate =>
                candidate.Name == name);
            Assert.That(function.DeclaredRequiresUnsafeCaller,
                Is.EqualTo(declaredUnsafe));
            Assert.That(function.RequiresUnsafeCaller, Is.True);
            var functionPointerType = function.NativeFunctionPointerType
                ?? throw new InvalidOperationException(
                    $"NativeCallable '{name}' has no address-contract root.");
            var signature = reference.NativeFunctionPointerSignatures[
                NativeFunctionPointerTypes.Index(functionPointerType)];
            Assert.That(signature.EffectsMask, Is.EqualTo(expectedMask));
        }
    }

    [Test]
    public void BodylessNativeCallableEntryBindsToItsOriginalOwner()
    {
        var provider = CreateCallableEntryProducer();
        var renamedProvider = CreateCallableEntryProducer("CD");
        var reference = ModuleCompiler.CreateReference(provider);
        var compilation = CreateConsumer(SyntaxTree.Parse("""
            int32 Main() {
                unsafe {
                    [Terrum.Interop.NativeCallConv(
                        Terrum.Interop.NativeCallingConvention.Unmanaged)]
                    fnptr<int32, int32> callback = &Identity;
                    return callback(42);
                }
            }
            """), reference);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(compilation.Program!,
            provider);

        Assert.DoesNotThrow(() => ModuleCompiler.Link(consumer, provider));
        _ = Assert.Throws<ModuleContractException>(() =>
            ModuleCompiler.Link(consumer, renamedProvider));
    }

    private static ProviderArtifacts CreateProvider(bool reverse,
        bool changeIdentity)
    {
        var identity = changeIdentity
            ? """
                [Terrum.Interop.NativeCallable]
                public int64 Identity(int64 value) { return value; }
                """
            : """
                [Terrum.Interop.NativeCallable]
                public int32 Identity(int32 value) { return value; }
                """;
        const string different = """
            [Terrum.Interop.NativeCallable]
            public int64 Different(int64 value) { return value; }
            """;
        var source = reverse ? different + identity : identity + different;
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source),
            NativeCoreRegistrationTests.CreateRegisteredCompilationOptions());
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var exports = new[] { "Identity", "Different" };
        var privateProducer = ModuleCompiler.CreateProducer(
            compilation.Program!, exports);
        var nativeProducer = NativeModuleCompiler.CreateProducer(
            compilation.Program!, exports, "CallableProvider",
            NativeRuntimePolicy.FailFast);
        return new ProviderArtifacts(privateProducer, nativeProducer);
    }

    private static byte[] CreateExtraProvider()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            "public int32 Extra() { return 0; }"));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        return NativeModuleCompiler.CreateProducer(compilation.Program!,
            ["Extra"], "ExtraProvider", NativeRuntimePolicy.FailFast);
    }

    private static byte[] CreateCallableEntryProducer(string entryName = "AB")
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Terrum.Interop.NativeCallable]
            public int32 Identity(int32 value) { return value; }
            """), NativeCoreRegistrationTests.CreateRegisteredCompilationOptions());
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program!;
        var index = program.Functions.IndexOf(
            program.Functions.Single(static function =>
                function.Name == "Identity"));
        program = program with {
            Functions = program.Functions.SetItem(index,
                program.Functions[index] with {
                    NativeCallableEntryName = entryName,
                }),
        };
        return ModuleCompiler.CreateProducer(program, ["Identity"]);
    }

    private static Compilation CreateConsumer(SyntaxTree tree,
        CompilationReference? reference = null)
    {
        var options = NativeCoreRegistrationTests.CreateRegisteredCompilationOptions();
        return reference is null
            ? Compilation.Create(tree, options)
            : Compilation.Create(tree, reference, options);
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
            "The NativeAddressContract token was not found.");
    }

    private sealed record ProviderArtifacts(byte[] Private, byte[] Native);
}
