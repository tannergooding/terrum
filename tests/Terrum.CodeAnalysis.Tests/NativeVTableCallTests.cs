using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed class NativeVTableCallTests
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NativeVTableDispatch(IntPtr receiver);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NativeVTableOutputDispatch(IntPtr receiver, IntPtr output);

    [Test]
    public void NativeVTableDeclarationRoundTripsThroughStandardTrRm()
    {
        var provider = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Terrum.Interop.NativeParameter(2, Read, NoEscape, 1)]
            [Terrum.Interop.NativeVTableMethod(3)]
            public extern unsafe uint32 Invoke(ptr<void> receiver, ptr<uint32> value);
            """));
        Assert.That(provider.HasErrors, Is.False,
            string.Join(Environment.NewLine, provider.Diagnostics));

        var artifact = TrRmExecutableCompiler.Emit(provider.Program!, "native-vtable");
        var index = new TrRmArtifactIndex(artifact);
        Assert.That(index.TryGetSection(0x0032, out _, out var vtableCount), Is.True);
        Assert.That(vtableCount, Is.EqualTo(1));
        AssertNativeVTableFeatures(index);

        var loaded = TrRmExecutableCompiler.Load(artifact);
        var method = loaded.Functions.Single(static function => function.Name == "Invoke");
        Assert.That(method.Intrinsic, Is.EqualTo(BoundIntrinsic.NativeVTableCall));
        Assert.That(method.IsExternal, Is.False);
        Assert.That(method.RequiresUnsafeCaller, Is.True);

        var vtableSectionOffset = FindSectionOffset(artifact, 0x0032);

        var malformed = (byte[])artifact.Clone();
        BinaryPrimitives.WriteUInt32LittleEndian(malformed.AsSpan(vtableSectionOffset + 8), 3);
        Assert.Throws<InvalidDataException>(() => TrRmExecutableCompiler.Load(malformed));

        BinaryPrimitives.WriteUInt32LittleEndian(artifact.AsSpan(vtableSectionOffset + 8), 8);
        var offsetLoaded = TrRmExecutableCompiler.Load(artifact);
        var reemitted = TrRmExecutableCompiler.Emit(offsetLoaded, "native-vtable-offset");
        var reemittedIndex = new TrRmArtifactIndex(reemitted);
        Assert.That(reemittedIndex.TryGetSection(0x0032, out var reemittedVTables, out _), Is.True);
        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(reemittedVTables[8..]), Is.EqualTo(8));

        var reference = TrRmExecutableCompiler.CreateReference(reemitted);
        var consumer = Compilation.Create(SyntaxTree.Parse("""
            int32 Dispatch(ptr<void> receiver) {
                mut uint32 value = 7;
                unsafe { uint32 result = Invoke(receiver, &value); return 0; }
            }

            int32 Main() { return 0; }
            """), reference);
        Assert.That(consumer.HasErrors, Is.False,
            string.Join(Environment.NewLine, consumer.Diagnostics));
        var addressConsumerArtifact = TrRmExecutableCompiler.Emit(
            consumer.Program!, "native-vtable-address-consumer");
        AssertNativeVTableFeatures(new TrRmArtifactIndex(addressConsumerArtifact),
            hasScalarInitOutput: true);
        var addressConsumer = TrRmExecutableCompiler.Load(addressConsumerArtifact);
        var loadedDispatch = addressConsumer.Functions
            .Single(static function => function.Name == "Dispatch");
        Assert.That(loadedDispatch.Variables[0].Type, Is.EqualTo(PrimitiveType.NativeRawPointer));
        Assert.That(loadedDispatch.Body.Statements, Is.Not.Empty);
        Assert.That(NativeEmitter.Emit(
            addressConsumer, NativeRuntimePolicy.FailFast), Is.Not.Empty);
        var reemittedAddressConsumer = TrRmExecutableCompiler.Emit(addressConsumer,
            "native-vtable-address-consumer-reloaded");
        var reloadedAddressConsumer = TrRmExecutableCompiler.Load(reemittedAddressConsumer);
        Assert.That(NativeEmitter.Emit(
            reloadedAddressConsumer, NativeRuntimePolicy.FailFast), Is.Not.Empty);

        var caller = Compilation.Create(SyntaxTree.Parse("""
            [Terrum.Interop.NativeVTableMethod(3)]
            extern unsafe ptr<void> Get(ptr<void> receiver);

            [Terrum.Interop.NativeVTableMethod(4)]
            extern unsafe uint32 Invoke(ptr<void> receiver);

            int32 Main() {
                unsafe { return Invoke(Get(null)) == 9 ? 0 : 1; }
            }
            """));
        Assert.That(caller.HasErrors, Is.False,
            string.Join(Environment.NewLine, caller.Diagnostics));
        var callArtifact = TrRmExecutableCompiler.Emit(caller.Program!, "native-vtable-call");
        var callIndex = new TrRmArtifactIndex(callArtifact);
        AssertNativeVTableFeatures(callIndex);
        var callLoaded = TrRmExecutableCompiler.Load(callArtifact);
        Assert.That(callLoaded.EntryPoint, Is.GreaterThanOrEqualTo(0));

        var bodyIndex = callIndex;
        Assert.That(bodyIndex.TryGetSection(0x0020, out _, out var bodyCount), Is.True);
        Assert.That(bodyCount, Is.EqualTo(1));
        var bodySectionOffset = FindSectionOffset(callArtifact, 0x0020);
        var bodyDataOffset = bodySectionOffset + ((bodyCount + 1) * sizeof(uint));
        var bodyStart = bodyDataOffset + checked((int)BinaryPrimitives.ReadUInt32LittleEndian(
            callArtifact.AsSpan(bodySectionOffset)));
        var bodyEnd = bodyDataOffset + checked((int)BinaryPrimitives.ReadUInt32LittleEndian(
            callArtifact.AsSpan(bodySectionOffset + sizeof(uint))));
        var malformedPointerCall = (byte[])callArtifact.Clone();
        var pointerConstantOffset = -1;
        var bodyBytes = malformedPointerCall.AsSpan(bodyStart, bodyEnd - bodyStart);
        for (var byteIndex = 0; byteIndex < bodyBytes.Length - 3; byteIndex++)
        {
            if (bodyBytes[byteIndex] == 0x01
                && bodyBytes[byteIndex + 2] == 7 && bodyBytes[byteIndex + 3] == 0)
            {
                Assert.That(pointerConstantOffset, Is.EqualTo(-1));
                pointerConstantOffset = byteIndex + 3;
            }
        }
        Assert.That(pointerConstantOffset, Is.GreaterThanOrEqualTo(0));
        bodyBytes[pointerConstantOffset] = 1;
        Assert.Throws<InvalidDataException>(() => TrRmExecutableCompiler.Load(malformedPointerCall));

        var missingVTableFeatures = (byte[])callArtifact.Clone();
        var moduleOffset = FindSectionOffset(callArtifact, 0x0001);
        BinaryPrimitives.WriteUInt32LittleEndian(missingVTableFeatures.AsSpan(moduleOffset + 20), 1);
        Assert.Throws<InvalidDataException>(() => TrRmExecutableCompiler.Load(missingVTableFeatures));

        var privateArtifact = ModuleCompiler.CreateProducer(offsetLoaded, ["Invoke"]);
        Assert.That(BinaryPrimitives.ReadInt32LittleEndian(privateArtifact.AsSpan(4)), Is.EqualTo(86));
        var privateReference = ModuleCompiler.CreateReference(privateArtifact);
        var privateConsumer = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() {
                mut uint32 value = 7;
                unsafe { return Invoke(null, &value) == 9 ? 0 : 1; }
            }
            """), privateReference);
        Assert.That(privateConsumer.HasErrors, Is.False,
            string.Join(Environment.NewLine, privateConsumer.Diagnostics));
    }

    [Test]
    public void NativeVTableIndexedBodyRoundTripsTypedPointerArguments()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            [Terrum.Interop.NativeVTableMethod(0)]
            extern unsafe uint32 Invoke(ptr<uint32> receiver);

            uint32 Dispatch(ptr<uint32> receiver, ptr<int32> mismatched, bool invoke) {
                unsafe {
                    if (invoke) { return Invoke(receiver); }
                    return 0;
                }
            }

            int32 Main() { return 0; }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var artifact = TrRmExecutableCompiler.Emit(compilation.Program!,
            "native-vtable-indexed-pointer-arguments");
        AssertNativeVTableFeatures(new TrRmArtifactIndex(artifact));
        var loaded = TrRmExecutableCompiler.Load(artifact);
        var dispatch = loaded.Functions.Single(static function => function.Name == "Dispatch");
        var invoke = loaded.Functions.Single(static function => function.Name == "Invoke");
        Assert.That(NativePointerTypes.IsType(dispatch.Variables[0].Type), Is.True);
        Assert.That(dispatch.Variables[0].Type, Is.EqualTo(invoke.Variables[0].Type));
        Assert.That(loaded.NativePointerTypes[NativePointerTypes.Index(dispatch.Variables[0].Type)]
            .PointeeType, Is.EqualTo(PrimitiveType.UInt32));
        Assert.That(NativeEmitter.Emit(loaded, NativeRuntimePolicy.FailFast), Is.Not.Empty);

        var index = new TrRmArtifactIndex(artifact);
        var tables = TrRmStructuralMetadataValidator.ValidateAndGetExecutionTables(index);
        var invokeRow = 0;
        for (var row = 1; row <= tables.Methods.Count; row++)
        {
            if (tables.Methods.GetName(row) == "Invoke")
            {
                invokeRow = row;
                break;
            }
        }
        Assert.That(invokeRow, Is.GreaterThan(0));
        var (firstParameter, parameterCount) = tables.Signatures.GetParamRange(
            tables.Methods.GetSignature(invokeRow));
        Assert.That(parameterCount, Is.EqualTo(1));
        var signedPointerTypeSpec = 0;
        for (var row = 1; row <= tables.TypeSpecs.Count; row++)
        {
            var type = tables.TypeSpecs.GetRow(row);
            if (type.Length == 4 && type[0] == 1 && type[2] == 0x8C && type[3] == 8)
            {
                signedPointerTypeSpec = row;
                break;
            }
        }
        Assert.That(signedPointerTypeSpec, Is.GreaterThan(0));
        Assert.That(tables.TypeSpecs.GetCompactValueTypeKey(signedPointerTypeSpec),
            Is.Not.EqualTo(tables.TypeSpecs.GetCompactValueTypeKey(
                tables.Parameters.GetTypeSpec(firstParameter))));

        var malformed = (byte[])artifact.Clone();
        var parameterSectionOffset = FindSectionOffset(artifact, 0x18);
        var parameterTypeOffset = checked(parameterSectionOffset
            + ((firstParameter - 1) * 16) + 8);
        BinaryPrimitives.WriteUInt32LittleEndian(malformed.AsSpan(parameterTypeOffset),
            (uint)signedPointerTypeSpec);
        Assert.Throws<InvalidDataException>(() => TrRmExecutableCompiler.Load(malformed));
    }

    [Test]
    public void NativeVTableVoidDeclarationRoundTripsThroughStandardTrRm()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [NoThrow]
            [Terrum.Interop.NativeVTableMethod(3)]
            public extern unsafe void Release(ptr<void> receiver);
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var artifact = TrRmExecutableCompiler.Emit(compilation.Program!, "native-vtable-void");
        var loaded = TrRmExecutableCompiler.Load(artifact);
        var release = loaded.Functions.Single(static function => function.Name == "Release");
        Assert.That(release.ReturnType, Is.EqualTo(PrimitiveType.Void));
        Assert.That(release.Intrinsic, Is.EqualTo(BoundIntrinsic.NativeVTableCall));
        Assert.That(release.RequiresUnsafeCaller, Is.True);
        Assert.That(release.NativeVTableMethod, Is.Not.Null);
        Assert.That(TrRmExecutableCompiler.Emit(loaded, "native-vtable-void"), Is.EqualTo(artifact));

        var reference = TrRmExecutableCompiler.CreateReference(artifact);
        Assert.That(reference.Functions.Single(static function => function.Name == "Release").ReturnType,
            Is.EqualTo(PrimitiveType.Void));
    }

    [Test]
    public void NativeVTableTrRmImportRebindsAndValidatesNativeProviders()
    {
        var provider = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Terrum.Interop.NativeParameter(2, Read, NoEscape, 1)]
            [Terrum.Interop.NativeVTableMethod(0)]
            public extern unsafe int32 Invoke(ptr<void> receiver, ptr<uint32> value);
            """));
        Assert.That(provider.HasErrors, Is.False,
            string.Join(Environment.NewLine, provider.Diagnostics));

        var providerArtifact = TrRmExecutableCompiler.Emit(provider.Program!, "VTableProvider");
        var reference = TrRmExecutableCompiler.CreateReference(providerArtifact);
        var consumer = Compilation.Create(SyntaxTree.Parse("""
            int32 Dispatch() {
                unsafe { return Invoke(null, null); }
            }
            int32 Main() { return 0; }
            """), reference);
        Assert.That(consumer.HasErrors, Is.False,
            string.Join(Environment.NewLine, consumer.Diagnostics));
        Assert.That(consumer.Program!.Functions.Single(static function => function.Name == "Invoke")
            .ModuleImport, Is.EqualTo(new BoundModuleImport("VTableProvider", "Invoke", 1, 0)));

        var consumerArtifact = TrRmExecutableCompiler.Emit(consumer.Program!, "vtable-consumer");
        var consumerIndex = new TrRmArtifactIndex(consumerArtifact);
        Assert.That(consumerIndex.TryGetSection(0x001C, out _, out var importCount), Is.True);
        Assert.That(importCount, Is.EqualTo(1));
        var loaded = TrRmExecutableCompiler.Load(consumerArtifact);
        var importedDispatch = loaded.Functions.Single(static function => function.Name == "Invoke");
        Assert.That(importedDispatch.Intrinsic, Is.EqualTo(BoundIntrinsic.NativeVTableCall));
        Assert.That(importedDispatch.IsExternal, Is.False);
        Assert.That(importedDispatch.ModuleImport, Is.EqualTo(
            new BoundModuleImport("VTableProvider", "Invoke", 1, 0)));
        Assert.That(loaded.Functions.Single(static function => function.Name == "Dispatch")
            .IndexedBody, Is.Not.Null);
        Assert.That(NativeEmitter.Emit(loaded, NativeRuntimePolicy.FailFast), Is.Not.Empty);

        var nativeProvider = NativeModuleCompiler.CreateProducer(provider.Program!,
            ["Invoke"], "VTableProvider", NativeRuntimePolicy.FailFast);
        var extraProviderCompilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            "public int32 Extra() { return 43; }"));
        Assert.That(extraProviderCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, extraProviderCompilation.Diagnostics));
        var extraNativeProvider = NativeModuleCompiler.CreateProducer(extraProviderCompilation.Program!,
            ["Extra"], "ExtraVTableDependency", NativeRuntimePolicy.FailFast);
        var providers = new[] { nativeProvider, extraNativeProvider };
        var nativeConsumer = NativeModuleCompiler.CreateConsumer(consumer.Program!,
            nativeProvider, NativeRuntimePolicy.FailFast);
        NativeModuleCompiler.Validate(nativeConsumer, nativeProvider);

        const string setConsumerSource = """
            int32 Dispatch() {
                unsafe { return Invoke(null, null); }
            }
            int32 Main() {
                if (Dispatch() != 0) { return -1; }
                return Extra() == 43 ? 0 : -2;
            }
            """;
        var setCompilation = Compilation.Create(SyntaxTree.Parse(setConsumerSource),
            NativeModuleCompiler.CreateReferenceSet(providers));
        Assert.That(setCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, setCompilation.Diagnostics));
        var nativeConsumerSet = NativeModuleCompiler.CreateConsumerSet(
            setCompilation.Program!, providers, NativeRuntimePolicy.FailFast);
        NativeModuleCompiler.ValidateConsumerSet(nativeConsumerSet, providers);

        var graphConsumer = NativeModuleCompiler.CreateGraphConsumer(
            setCompilation.Program!, providers, NativeRuntimePolicy.FailFast);
        NativeModuleCompiler.ValidateGraph(graphConsumer, providers);

        var graphLibraryCompilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            public int32 Forward() {
                unsafe { return Invoke(null, null); }
            }
            """), reference);
        Assert.That(graphLibraryCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, graphLibraryCompilation.Diagnostics));
        var graphLibrary = NativeModuleCompiler.CreateGraphProducer(
            graphLibraryCompilation.Program!, ["Forward"], "VTableMiddle",
            [nativeProvider], NativeRuntimePolicy.FailFast);
        _ = NativeModuleCompiler.CreateGraphReference([graphLibrary]);

        var wrongIdentityProvider = NativeModuleCompiler.CreateProducer(provider.Program!,
            ["Invoke"], "WrongVTableProvider", NativeRuntimePolicy.FailFast);
        Assert.Throws<ModuleContractException>(() => NativeModuleCompiler.CreateConsumer(
            consumer.Program!, wrongIdentityProvider, NativeRuntimePolicy.FailFast));
        Assert.Throws<ModuleContractException>(() => NativeModuleCompiler.CreateGraphProducer(
            graphLibraryCompilation.Program!, ["Forward"], "VTableMiddle",
            [wrongIdentityProvider], NativeRuntimePolicy.FailFast));

        var wrongSlotCompilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Terrum.Interop.NativeParameter(2, Read, NoEscape, 1)]
            [Terrum.Interop.NativeVTableMethod(1)]
            public extern unsafe int32 Invoke(ptr<void> receiver, ptr<uint32> value);
            """));
        Assert.That(wrongSlotCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, wrongSlotCompilation.Diagnostics));
        var wrongSlotProvider = NativeModuleCompiler.CreateProducer(wrongSlotCompilation.Program!,
            ["Invoke"], "VTableProvider", NativeRuntimePolicy.FailFast);
        Assert.Throws<ModuleContractException>(() => NativeModuleCompiler.CreateConsumer(
            consumer.Program!, wrongSlotProvider, NativeRuntimePolicy.FailFast));
        Assert.Throws<ModuleContractException>(() =>
            NativeModuleCompiler.Validate(nativeConsumer, wrongSlotProvider));
        Assert.Throws<ModuleContractException>(() => NativeModuleCompiler.CreateGraphProducer(
            graphLibraryCompilation.Program!, ["Forward"], "VTableMiddle",
            [wrongSlotProvider], NativeRuntimePolicy.FailFast));
        Assert.Throws<ModuleContractException>(() => NativeModuleCompiler.CreateConsumerSet(
            setCompilation.Program!, [wrongSlotProvider, extraNativeProvider], NativeRuntimePolicy.FailFast));
        Assert.Throws<ModuleContractException>(() => NativeModuleCompiler.ValidateConsumerSet(
            nativeConsumerSet, [wrongSlotProvider, extraNativeProvider]));
        Assert.Throws<ModuleContractException>(() => NativeModuleCompiler.CreateGraphConsumer(
            setCompilation.Program!, [wrongSlotProvider, extraNativeProvider], NativeRuntimePolicy.FailFast));
        Assert.Throws<ModuleContractException>(() => NativeModuleCompiler.ValidateGraph(
            graphConsumer, [wrongSlotProvider, extraNativeProvider]));
    }

    [Test]
    public async Task NativeVTableTrRmImportDispatchesNonNullReceiverAsync()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        var provider = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [NoThrow]
            [Terrum.Interop.NativeVTableMethod(0)]
            public extern unsafe int32 Invoke(ptr<void> receiver);
            """));
        Assert.That(provider.HasErrors, Is.False,
            string.Join(Environment.NewLine, provider.Diagnostics));

        var artifact = TrRmExecutableCompiler.Emit(provider.Program!, "VTableProvider");
        var reference = TrRmExecutableCompiler.CreateReference(artifact);
        var consumer = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [NoThrow]
            [Terrum.Interop.NativeCallable]
            public int32 GetValue(ptr<void> receiver) { return 42; }

            [NoThrow]
            [Terrum.Interop.NativeCallable]
            public unsafe int32 Dispatch(ptr<void> receiver) {
                unsafe { return Invoke(receiver); }
            }
            """), reference);
        Assert.That(consumer.HasErrors, Is.False,
            string.Join(Environment.NewLine, consumer.Diagnostics));

        var libraryPath = Path.Combine(Path.GetTempPath(), $"terrum-trrm-vtable-{Guid.NewGuid():N}.dll");
        var handle = IntPtr.Zero;
        var vtable = IntPtr.Zero;
        var receiver = IntPtr.Zero;
        try
        {
            var consumerArtifact = TrRmExecutableCompiler.Emit(consumer.Program!, "VTableConsumer");
            var loadedConsumer = TrRmExecutableCompiler.Load(consumerArtifact);
            await File.WriteAllBytesAsync(libraryPath, NativeEmitter.EmitLibrary(
                loadedConsumer, ["GetValue", "Dispatch"], NativeRuntimePolicy.FailFast))
                .ConfigureAwait(true);
            handle = NativeLibrary.Load(libraryPath);
            vtable = Marshal.AllocHGlobal(IntPtr.Size);
            receiver = Marshal.AllocHGlobal(IntPtr.Size);
            Marshal.WriteIntPtr(vtable, NativeLibrary.GetExport(handle, "GetValue"));
            Marshal.WriteIntPtr(receiver, vtable);

            var dispatch = Marshal.GetDelegateForFunctionPointer<NativeVTableDispatch>(
                NativeLibrary.GetExport(handle, "Dispatch"));
            Assert.That(dispatch(receiver), Is.EqualTo(42));
        }
        finally
        {
            if (handle != IntPtr.Zero)
            {
                NativeLibrary.Free(handle);
            }
            if (receiver != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(receiver);
            }
            if (vtable != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(vtable);
            }
            await TestFileCleanup.DeleteWithRetryAsync(libraryPath).ConfigureAwait(true);
        }
    }

    [Test]
    public async Task NativeVTableTrRmOutputWritesThroughNonNullReceiverAsync()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        var provider = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [NoThrow]
            [Terrum.Interop.NativeVTableMethod(0)]
            [Terrum.Interop.NativeOutput(1, NonNegative)]
            public extern unsafe int32 Invoke(ptr<void> receiver, write ptr<uint32> output);
            """));
        Assert.That(provider.HasErrors, Is.False,
            string.Join(Environment.NewLine, provider.Diagnostics));

        var providerArtifact = TrRmExecutableCompiler.Emit(provider.Program!, "VTableOutputProvider");
        var loadedProvider = TrRmExecutableCompiler.Load(providerArtifact);
        var loadedMethod = loadedProvider.Functions.Single(static function => function.Name == "Invoke");
        Assert.That(loadedMethod.NativeVTableMethod!.Outputs.Single(),
            Is.EqualTo(new DirectNativeOutput(1, PrimitiveType.UInt32, 3, 1)));

        var contractIndex = new TrRmArtifactIndex(providerArtifact);
        Assert.That(contractIndex.TryGetSection(0x001B, out _, out var contractCount), Is.True);
        var contractSectionOffset = FindSectionOffset(providerArtifact, 0x001B);
        var contractDataOffset = contractSectionOffset + ((contractCount + 1) * sizeof(uint));
        var malformedProvider = (byte[])providerArtifact.Clone();
        var mutatedOutput = false;
        for (var row = 1; row <= contractCount; row++)
        {
            var rowStart = contractDataOffset + checked((int)BinaryPrimitives.ReadUInt32LittleEndian(
                malformedProvider.AsSpan(contractSectionOffset + ((row - 1) * sizeof(uint)))));
            if (BinaryPrimitives.ReadUInt16LittleEndian(malformedProvider.AsSpan(rowStart + 4)) == 0x12)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(malformedProvider.AsSpan(rowStart + 8), 0);
                mutatedOutput = true;
            }
        }
        Assert.That(mutatedOutput, Is.True);
        Assert.Throws<InvalidDataException>(() => TrRmExecutableCompiler.Load(malformedProvider));

        var semanticProvider = ModuleCompiler.CreateProducer(loadedProvider, ["Invoke"]);
        Assert.That(BinaryPrimitives.ReadInt32LittleEndian(semanticProvider.AsSpan(4)), Is.EqualTo(87));
        var reference = TrRmExecutableCompiler.CreateReference(providerArtifact);
        var consumer = Compilation.CreateLibrary(SyntaxTree.Parse("""
            bool Failed(int32 status) { return status < 0; }
            public unsafe int32 Dispatch(ptr<void> receiver) {
                mut uint32 output = 0;
                unsafe {
                    int32 status = Invoke(receiver, &output);
                    if (Failed(status)) { return -1; }
                    else { return int32(output); }
                }
            }
            """), reference);
        Assert.That(consumer.HasErrors, Is.False,
            string.Join(Environment.NewLine, consumer.Diagnostics));

        var consumerArtifact = TrRmExecutableCompiler.Emit(consumer.Program!, "VTableOutputConsumer");
        AssertNativeVTableFeatures(new TrRmArtifactIndex(consumerArtifact), hasScalarInitOutput: true);
        var loadedConsumer = TrRmExecutableCompiler.Load(consumerArtifact);
        var privateReference = ModuleCompiler.CreateReference(semanticProvider);
        var privateConsumer = Compilation.CreateLibrary(SyntaxTree.Parse("""
            bool Failed(int32 status) { return status < 0; }
            public unsafe int32 Dispatch(ptr<void> receiver) {
                mut uint32 output = 0;
                unsafe {
                    int32 status = Invoke(receiver, &output);
                    if (Failed(status)) { return -1; }
                    else { return int32(output); }
                }
            }
            """), privateReference);
        Assert.That(privateConsumer.HasErrors, Is.False,
            string.Join(Environment.NewLine, privateConsumer.Diagnostics));

        var libraryPath = Path.Combine(Path.GetTempPath(), $"terrum-trrm-vtable-output-{Guid.NewGuid():N}.dll");
        var handle = IntPtr.Zero;
        var vtable = IntPtr.Zero;
        var receiver = IntPtr.Zero;
        NativeVTableOutputDispatch outputCallback = static (_, output) =>
        {
            Marshal.WriteInt32(output, 42);
            return 0;
        };
        try
        {
            await File.WriteAllBytesAsync(libraryPath, NativeEmitter.EmitLibrary(
                loadedConsumer, ["Dispatch"], NativeRuntimePolicy.FailFast))
                .ConfigureAwait(true);
            handle = NativeLibrary.Load(libraryPath);
            vtable = Marshal.AllocHGlobal(IntPtr.Size);
            receiver = Marshal.AllocHGlobal(IntPtr.Size);
            Marshal.WriteIntPtr(vtable, Marshal.GetFunctionPointerForDelegate(outputCallback));
            Marshal.WriteIntPtr(receiver, vtable);

            var dispatch = Marshal.GetDelegateForFunctionPointer<NativeVTableDispatch>(
                NativeLibrary.GetExport(handle, "Dispatch"));
            Assert.That(dispatch(receiver), Is.EqualTo(42));
            GC.KeepAlive(outputCallback);
        }
        finally
        {
            if (handle != IntPtr.Zero)
            {
                NativeLibrary.Free(handle);
            }
            if (receiver != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(receiver);
            }
            if (vtable != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(vtable);
            }
            await TestFileCleanup.DeleteWithRetryAsync(libraryPath).ConfigureAwait(true);
        }
    }

    private static string CompilerApphost()
    {
        var framework = new DirectoryInfo(AppContext.BaseDirectory);
        var configuration = framework.Parent?.Name
            ?? throw new InvalidOperationException("Missing test configuration directory.");
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
        return Path.Combine(root, "artifacts", "bin", "sources", "terrumc",
            configuration, framework.Name, "terrumc.exe");
    }

    private static int FindSectionOffset(byte[] artifact, ushort kind)
    {
        var sectionCount = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(artifact.AsSpan(12)));
        for (var section = 0; section < sectionCount; section++)
        {
            var directoryOffset = 32 + (section * 16);
            if (BinaryPrimitives.ReadUInt16LittleEndian(artifact.AsSpan(directoryOffset)) == kind)
            {
                return checked((int)BinaryPrimitives.ReadUInt32LittleEndian(
                    artifact.AsSpan(directoryOffset + 8)));
            }
        }

        throw new InvalidDataException($"Missing TRRM section 0x{kind:X4}.");
    }

    private static void AssertNativeVTableFeatures(TrRmArtifactIndex index,
        bool hasScalarInitOutput = false)
    {
        Assert.That(index.TryGetSection(0x0004, out var featureBytes, out var featureCount), Is.True);
        var features = new TrRmFeatureTableReader(featureBytes, featureCount);
        Assert.That(featureCount, Is.EqualTo(hasScalarInitOutput ? 4 : 3));
        var featureIds = new uint[featureCount];
        for (var row = 1; row <= featureCount; row++)
        {
            featureIds[row - 1] = features.GetFeature(row).FeatureId;
        }
        Assert.That(featureIds,
            Does.Contain(TrRmFeatureTableReader.NativeBoundaryContractsFeatureId));
        Assert.That(featureIds,
            Does.Contain(TrRmFeatureTableReader.RootlessNativeValuesFeatureId));
        Assert.That(featureIds,
            Does.Contain(TrRmFeatureTableReader.NativeVTableCallsFeatureId));
        Assert.That(featureIds.Contains(TrRmFeatureTableReader.ScalarInitOutputFeatureId),
            Is.EqualTo(hasScalarInitOutput));
    }

    [Test]
    public void NativeVTableSlotForwardsExplicitReceiverAndWindowsX64ArgumentPositions()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            [Blittable] [Layout(LayoutKind.Sequential)]
            struct TestVTable {
                public fnptr<NativeAbi, ptr<TestObject>, uint32, float64, uint32, float64, float64, uint32, uint32> Method;
                public TestVTable(fnptr<NativeAbi, ptr<TestObject>, uint32, float64, uint32, float64, float64, uint32, uint32> method) {
                    Method = method;
                }
            }

            [Blittable] [Layout(LayoutKind.Sequential)]
            struct TestObject {
                public ptr<TestVTable> VTable;
                public uint32 Marker;
                public TestObject(ptr<TestVTable> vtable, uint32 marker) {
                    VTable = vtable;
                    Marker = marker;
                }
            }

            [NoThrow]
            [Terrum.Interop.NativeCallable]
            uint32 GetValue(ptr<TestObject> receiver, uint32 first, float64 second,
                uint32 third, float64 fourth, float64 fifth, uint32 sixth) {
                unsafe {
                    return (*receiver).Marker == 7 && first == 10 && second == 1.25
                        && third == 20 && fourth == 2.5 && fifth == 3.75 && sixth == 30 ? 42 : 0;
                }
            }

            [Terrum.Interop.NativeVTableMethod(0)]
            extern unsafe uint32 Invoke(ptr<TestObject> receiver, uint32 first, float64 second,
                uint32 third, float64 fourth, float64 fifth, uint32 sixth);

            int32 Main() {
                unsafe {
                    TestVTable vtable = new TestVTable(&GetValue);
                    TestObject instance = new TestObject(&vtable, 7);
                    ptr<TestObject> receiver = &instance;
                    return Invoke(receiver, 10, 1.25, 20, 2.5, 3.75, 30) == 42 ? 0 : -1;
                }
            }
            """));

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var method = compilation.Program!.Functions
            .Single(static function => function.Name == "Invoke");
        Assert.That(method.Intrinsic, Is.EqualTo(BoundIntrinsic.NativeVTableCall));
        Assert.That(method.IsExternal, Is.False);
        Assert.That(method.RequiresUnsafeCaller, Is.True);

        Assert.That(NativeJit.Execute(compilation.Program, NativeRuntimePolicy.FailFast), Is.Zero);
    }

    [Test]
    public void NativeVTableMethodRequiresAReceiverPointerAndUnsafeCallers()
    {
        var invalidReceiver = Compilation.Create(SyntaxTree.Parse("""
            [Terrum.Interop.NativeVTableMethod(0)]
            extern unsafe int32 Invoke(int32 receiver);
            int32 Main() { return 0; }
            """));
        Assert.That(invalidReceiver.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "TRM3030"), Is.True);

        var unsafeCallerRequired = Compilation.Create(SyntaxTree.Parse("""
            [Terrum.Interop.NativeVTableMethod(0)]
            extern unsafe int32 Invoke(ptr<void> receiver);
            int32 Main() { return Invoke(null); }
            """));
        Assert.That(unsafeCallerRequired.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "TRM3028"), Is.True);
    }

    [Test]
    public void NativeVTableSlotReturnsFloatingValueAndCommitsOutputArgument()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            [Blittable] [Layout(LayoutKind.Sequential)]
            struct TestVTable {
                public fnptr<NativeAbi, ptr<TestObject>, write ptr<uint32>, float64> Method;
                public TestVTable(fnptr<NativeAbi, ptr<TestObject>, write ptr<uint32>, float64> method) {
                    Method = method;
                }
            }

            [Blittable] [Layout(LayoutKind.Sequential)]
            struct TestObject {
                public ptr<TestVTable> VTable;
                public TestObject(ptr<TestVTable> vtable) { VTable = vtable; }
            }

            [NoThrow]
            [Terrum.Interop.NativeCallable]
            float64 GetValue(ptr<TestObject> receiver, write ptr<uint32> output) {
                unsafe {
                    *output = 42;
                    return 10.5;
                }
            }

            [Terrum.Interop.NativeVTableMethod(0)]
            extern unsafe float64 Invoke(ptr<TestObject> receiver, out uint32 output);

            int32 Main() {
                unsafe {
                    TestVTable vtable = new TestVTable(&GetValue);
                    TestObject instance = new TestObject(&vtable);
                    ptr<TestObject> receiver = &instance;
                    mut uint32 output = 0;
                    float64 result = Invoke(receiver, out output);
                    return output == 42 && result == 10.5 ? 0 : -1;
                }
            }
            """));

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(NativeJit.Execute(compilation.Program!, NativeRuntimePolicy.FailFast), Is.Zero);
    }

    [Test]
    public void NativeVTableMixedNativeAndTerrumOutputsPreserveScalarAndPointerValues()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            [Terrum.Interop.NativeImport("kernel32.dll", "GetCurrentProcess")]
            [NoThrow]
            extern unsafe ptr<void> GetCurrentProcess();

            [Blittable] [Layout(LayoutKind.Sequential)]
            struct TestVTable {
                public fnptr<NativeAbi, ptr<TestObject>, write ptr<uint32>, write ptr<ptr<void>>,
                    write ptr<uint32>, ptr<void>, int32> Method;
                public TestVTable(fnptr<NativeAbi, ptr<TestObject>, write ptr<uint32>,
                    write ptr<ptr<void>>, write ptr<uint32>, ptr<void>, int32> method) {
                    Method = method;
                }
            }

            [Blittable] [Layout(LayoutKind.Sequential)]
            struct TestObject {
                public ptr<TestVTable> VTable;
                public TestObject(ptr<TestVTable> vtable) { VTable = vtable; }
            }

            [NoThrow]
            [Terrum.Interop.NativeCallable]
            int32 GetValues(ptr<TestObject> receiver, write ptr<uint32> scalarOutput,
                write ptr<ptr<void>> pointerOutput, write ptr<uint32> ordinaryOutput,
                ptr<void> pointerValue) {
                unsafe {
                    *scalarOutput = 42;
                    *pointerOutput = pointerValue;
                    *ordinaryOutput = 99;
                    return 0;
                }
            }

            [Terrum.Interop.NativeOutput(1, NonNegative)]
            [Terrum.Interop.NativeOutput(2, NonNegative)]
            [Terrum.Interop.NativeVTableMethod(0)]
            extern unsafe int32 Invoke(ptr<TestObject> receiver,
                write ptr<uint32> scalarOutput, write ptr<ptr<void>> pointerOutput,
                out uint32 ordinaryOutput, ptr<void> pointerValue);

            int32 Main() {
                unsafe {
                    ptr<void> pointerValue = GetCurrentProcess();
                    TestVTable vtable = new TestVTable(&GetValues);
                    TestObject instance = new TestObject(&vtable);
                    ptr<TestObject> receiver = &instance;
                    mut uint32 scalarOutput = 0;
                    mut ptr<void> pointerOutput = null;
                    mut uint32 ordinaryOutput = 0;
                    int32 result = Invoke(receiver, &scalarOutput, &pointerOutput,
                        out ordinaryOutput, pointerValue);
                    if (result < 0) { return -1; }
                    if (ordinaryOutput != 99) { return -2; }
                    if (scalarOutput != 42) { return -3; }
                    if (pointerOutput != pointerValue) { return -4; }
                    return 0;
                }
            }
            """));

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(NativeJit.Execute(compilation.Program!, NativeRuntimePolicy.FailFast), Is.Zero);
    }

    [Test]
    public async Task NativeVTableBoolResultWithOutputPreservesTheReturnValueAsync()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        var providerCompilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Blittable] [Layout(LayoutKind.Sequential)]
            struct TestVTable {
                public fnptr<NativeAbi, ptr<TestObject>, write ptr<uint32>, bool> Method;
                public TestVTable(fnptr<NativeAbi, ptr<TestObject>, write ptr<uint32>, bool> method) {
                    Method = method;
                }
            }

            [Blittable] [Layout(LayoutKind.Sequential)]
            struct TestObject {
                public ptr<TestVTable> VTable;
                public TestObject(ptr<TestVTable> vtable) { VTable = vtable; }
            }

            [NoThrow]
            [Terrum.Interop.NativeCallable]
            bool GetValue(ptr<TestObject> receiver, write ptr<uint32> output) {
                unsafe {
                    *output = 42;
                    return true;
                }
            }

            [Terrum.Interop.NativeVTableMethod(0)]
            extern unsafe bool Invoke(ptr<TestObject> receiver, out uint32 output);

            public int32 Run() {
                unsafe {
                    TestVTable vtable = new TestVTable(&GetValue);
                    TestObject instance = new TestObject(&vtable);
                    ptr<TestObject> receiver = &instance;
                    mut uint32 output = 0;
                    return Invoke(receiver, out output) && output == 42 ? 0 : -1;
                }
            }
            """));
        Assert.That(providerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, providerCompilation.Diagnostics));
        var producer = NativeModuleCompiler.CreateProducer(providerCompilation.Program!, ["Run"],
            "BoolVTableOutput", NativeRuntimePolicy.FailFast);
        var consumerCompilation = Compilation.Create(SyntaxTree.Parse("int32 Main() { return Run(); }"),
            NativeModuleCompiler.CreateReference(producer));
        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
        var consumer = NativeModuleCompiler.CreateConsumer(consumerCompilation.Program!, producer,
            NativeRuntimePolicy.FailFast);

        Assert.That(await NativeModuleCompiler.RunAsync(consumer, producer, TimeSpan.FromSeconds(10),
            NativeRuntimePolicy.FailFast).ConfigureAwait(true), Is.Zero);
    }

    [Test]
    public void NativeVTableMethodAcceptsNulTerminatedStringArguments()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            [Blittable] [Layout(LayoutKind.Sequential)]
            struct TestVTable {
                public fnptr<NativeAbi, ptr<TestObject>, ptr<char16>, int32> Method;
                public TestVTable(fnptr<NativeAbi, ptr<TestObject>, ptr<char16>, int32> method) {
                    Method = method;
                }
            }

            [Blittable] [Layout(LayoutKind.Sequential)]
            struct TestObject {
                public ptr<TestVTable> VTable;
                public TestObject(ptr<TestVTable> vtable) { VTable = vtable; }
            }

            [NoThrow]
            [Terrum.Interop.NativeCallable]
            int32 ReadFirst(ptr<TestObject> receiver, ptr<char16> value) {
                unsafe { return *value == 'a' ? 42 : 0; }
            }

            [Terrum.Interop.NativeParameter(2, Read, NoEscape, 0)]
            [Terrum.Interop.NativeVTableMethod(0)]
            extern unsafe int32 Invoke(ptr<TestObject> receiver, ptr<char16> value);

            int32 Main() {
                unsafe {
                    TestVTable vtable = new TestVTable(&ReadFirst);
                    TestObject instance = new TestObject(&vtable);
                    return Invoke(&instance, utf16z("abc")) == 42 ? 0 : -1;
                }
            }
            """));

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(NativeJit.Execute(compilation.Program!, NativeRuntimePolicy.FailFast), Is.Zero);
    }

    [Test]
    public void NativeVTableMethodReturnsRawPointer()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            [Blittable] [Layout(LayoutKind.Sequential)]
            struct TestVTable {
                public fnptr<NativeAbi, ptr<TestObject>, ptr<void>> Method;
                public TestVTable(fnptr<NativeAbi, ptr<TestObject>, ptr<void>> method) {
                    Method = method;
                }
            }

            [Blittable] [Layout(LayoutKind.Sequential)]
            struct TestObject {
                public ptr<TestVTable> VTable;
                public TestObject(ptr<TestVTable> vtable) { VTable = vtable; }
            }

            [NoThrow]
            [Terrum.Interop.NativeCallable]
            ptr<void> GetValue(ptr<TestObject> receiver) { return null; }

            [Terrum.Interop.NativeVTableMethod(0)]
            extern unsafe ptr<void> Invoke(ptr<TestObject> receiver);

            unsafe ptr<void> Forward(ptr<void> pointer) { return pointer; }

            int32 Main() {
                unsafe {
                    TestVTable vtable = new TestVTable(&GetValue);
                    TestObject instance = new TestObject(&vtable);
                    ptr<TestObject> receiver = &instance;
                    return Forward(Invoke(receiver)) == null ? 0 : -1;
                }
            }
            """));

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var method = compilation.Program!.Functions
            .Single(static function => function.Name == "Invoke");
        Assert.That(method.ReturnType, Is.EqualTo(PrimitiveType.NativeRawPointer));

        Assert.That(NativeJit.Execute(compilation.Program, NativeRuntimePolicy.FailFast), Is.Zero);

        var safeReturn = Compilation.Create(SyntaxTree.Parse("""
            ptr<void> GetPointer() { return null; }
            int32 Main() { return 0; }
            """));
        Assert.That(safeReturn.HasErrors, Is.True);
    }

    [Test]
    public void NativeVTableMethodUsesAnnotatedPointerArgumentAddress()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            [Blittable] [Layout(LayoutKind.Sequential)]
            struct TestVTable {
                public fnptr<NativeAbi, ptr<TestObject>, ptr<uint32>, uint32> Method;
                public TestVTable(fnptr<NativeAbi, ptr<TestObject>, ptr<uint32>, uint32> method) {
                    Method = method;
                }
            }

            [Blittable] [Layout(LayoutKind.Sequential)]
            struct TestObject {
                public ptr<TestVTable> VTable;
                public uint32 Marker;
                public TestObject(ptr<TestVTable> vtable, uint32 marker) {
                    VTable = vtable;
                    Marker = marker;
                }
            }

            [NoThrow]
            [Terrum.Interop.NativeCallable]
            uint32 GetValue(ptr<TestObject> receiver, ptr<uint32> value) { return 74; }

            [Terrum.Interop.NativeParameter(2, Read, NoEscape, 1)]
            [Terrum.Interop.NativeVTableMethod(0)]
            extern unsafe uint32 Invoke(ptr<TestObject> receiver, ptr<uint32> value);

            int32 Main() {
                unsafe {
                    TestVTable vtable = new TestVTable(&GetValue);
                    TestObject instance = new TestObject(&vtable, 42);
                    ptr<TestObject> receiver = &instance;
                    uint32 value = 32;
                    return Invoke(receiver, &value) == 74 ? 0 : -1;
                }
            }
            """));

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(NativeJit.Execute(compilation.Program!, NativeRuntimePolicy.FailFast), Is.Zero);
    }

    [Test]
    public async Task NativeVTableMethodSurvivesPrivateAndNativeModuleDependenciesAsync()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        const string providerSource = """
            [Blittable] [Layout(LayoutKind.Sequential)]
            public struct TestVTable {
                public fnptr<NativeAbi, ptr<TestObject>, ptr<TestPayload>, int32> First;
                public fnptr<NativeAbi, ptr<TestObject>, ptr<TestPayload>, int32> Second;
                public TestVTable(fnptr<NativeAbi, ptr<TestObject>, ptr<TestPayload>, int32> first,
                    fnptr<NativeAbi, ptr<TestObject>, ptr<TestPayload>, int32> second) {
                    First = first;
                    Second = second;
                }
            }

            [Blittable] [Layout(LayoutKind.Sequential)]
            public struct TestObject {
                public ptr<TestVTable> VTable;
                public TestObject(ptr<TestVTable> vtable) { VTable = vtable; }
            }

            [Blittable] [Layout(LayoutKind.Sequential)]
            struct UnusedStructure { int32 Value; }

            [Blittable] [Layout(LayoutKind.Sequential)]
            public struct TestPayload {
                public int32 Value;
                public TestPayload(int32 value) { Value = value; }
            }

            [Terrum.Interop.NativeParameter(2, Read, NoEscape, 1)]
            [Terrum.Interop.NativeVTableMethod(0)]
            public extern unsafe int32 Invoke(ptr<TestObject> receiver, ptr<TestPayload> value);
            """;
        const string consumerSource = """
            [NoThrow]
            [Terrum.Interop.NativeCallable]
            int32 GetFirst(ptr<TestObject> receiver, ptr<TestPayload> value) { return 42; }

            [NoThrow]
            [Terrum.Interop.NativeCallable]
            int32 GetSecond(ptr<TestObject> receiver, ptr<TestPayload> value) { return 43; }

            int32 Main() {
                unsafe {
                    TestVTable vtable = new TestVTable(&GetFirst, &GetSecond);
                    TestObject instance = new TestObject(&vtable);
                    ptr<TestObject> receiver = &instance;
                    TestPayload value = new TestPayload(42);
                    return Invoke(receiver, &value) == 42 ? 0 : -1;
                }
            }
            """;
        string[] exports = ["Invoke", "TestVTable.#ctor", "TestObject.#ctor", "TestPayload.#ctor"];
        var providerCompilation = Compilation.CreateLibrary(SyntaxTree.Parse(providerSource));
        Assert.That(providerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, providerCompilation.Diagnostics));
        var providerProgram = providerCompilation.Program!;
        var producer = ModuleCompiler.CreateProducer(providerProgram, exports);
        var reference = ModuleCompiler.CreateReference(producer);
        var consumerCompilation = Compilation.Create(SyntaxTree.Parse(consumerSource), reference);
        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(consumerCompilation.Program!, producer);
        Assert.That(NativeJit.Execute(ModuleCompiler.Link(consumer, producer),
            NativeRuntimePolicy.FailFast), Is.Zero);

        var slotOneSource = providerSource.Replace("NativeVTableMethod(0)", "NativeVTableMethod(1)",
            StringComparison.Ordinal);
        var slotOneCompilation = Compilation.CreateLibrary(SyntaxTree.Parse(slotOneSource));
        Assert.That(slotOneCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, slotOneCompilation.Diagnostics));
        var slotOneProducer = ModuleCompiler.CreateProducer(slotOneCompilation.Program!, exports);
        var slotOneConsumer = ModuleCompiler.Link(consumer, slotOneProducer);
        Assert.That(NativeJit.Execute(slotOneConsumer, NativeRuntimePolicy.FailFast), Is.EqualTo(-1));

        var incompatibleSource = providerSource.Replace("extern unsafe int32 Invoke",
            "extern unsafe int64 Invoke", StringComparison.Ordinal);
        var incompatibleCompilation = Compilation.CreateLibrary(SyntaxTree.Parse(incompatibleSource));
        Assert.That(incompatibleCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, incompatibleCompilation.Diagnostics));
        var incompatibleProducer = ModuleCompiler.CreateProducer(incompatibleCompilation.Program!, exports);
        Assert.Throws<ModuleContractException>(() => ModuleCompiler.Link(consumer, incompatibleProducer));

        var nativeProducer = NativeModuleCompiler.CreateProducer(providerProgram, exports, "native_vtable",
            NativeRuntimePolicy.FailFast);
        var nativeReference = NativeModuleCompiler.CreateReference(nativeProducer);
        var nativeConsumerCompilation = Compilation.Create(SyntaxTree.Parse(consumerSource), nativeReference);
        Assert.That(nativeConsumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, nativeConsumerCompilation.Diagnostics));
        var nativeConsumer = NativeModuleCompiler.CreateConsumer(nativeConsumerCompilation.Program!,
            nativeProducer, NativeRuntimePolicy.FailFast);
        NativeModuleCompiler.Validate(nativeConsumer, nativeProducer);
        Assert.That(await NativeModuleCompiler.RunAsync(nativeConsumer, nativeProducer,
            TimeSpan.FromSeconds(10), NativeRuntimePolicy.FailFast), Is.Zero);

        var extraProviderCompilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            "public int32 Extra() { return 43; }"));
        Assert.That(extraProviderCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, extraProviderCompilation.Diagnostics));
        var extraNativeProducer = NativeModuleCompiler.CreateProducer(
            extraProviderCompilation.Program!, ["Extra"], "native_extra", NativeRuntimePolicy.FailFast);
        var nativeProviders = new[] { nativeProducer, extraNativeProducer };
        var multiProviderConsumerSource = consumerSource.Replace(
            "return Invoke(receiver, &value) == 42 ? 0 : -1;",
            "if (Invoke(receiver, &value) != 42) { return -1; }\n"
                + "                    return Extra() == 43 ? 0 : -2;",
            StringComparison.Ordinal);

        var setReference = NativeModuleCompiler.CreateReferenceSet(nativeProviders);
        var setCompilation = Compilation.Create(
            SyntaxTree.Parse(multiProviderConsumerSource), setReference);
        Assert.That(setCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, setCompilation.Diagnostics));
        var nativeSet = NativeModuleCompiler.CreateConsumerSet(setCompilation.Program!,
            nativeProviders, NativeRuntimePolicy.FailFast);
        NativeModuleCompiler.ValidateConsumerSet(nativeSet, nativeProviders);
        Assert.That(await NativeModuleCompiler.RunConsumerSetAsync(nativeSet, nativeProviders,
            TimeSpan.FromSeconds(15), NativeRuntimePolicy.FailFast), Is.Zero);

        var graphReference = NativeModuleCompiler.CreateGraphReference(nativeProviders);
        var graphCompilation = Compilation.Create(
            SyntaxTree.Parse(multiProviderConsumerSource), graphReference);
        Assert.That(graphCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, graphCompilation.Diagnostics));
        var graphConsumer = NativeModuleCompiler.CreateGraphConsumer(graphCompilation.Program!,
            nativeProviders, NativeRuntimePolicy.FailFast);
        NativeModuleCompiler.ValidateGraph(graphConsumer, nativeProviders);
        Assert.That(await NativeModuleCompiler.RunGraphJitAsync(graphConsumer, nativeProviders,
            CompilerApphost(), TimeSpan.FromSeconds(15), NativeRuntimePolicy.FailFast), Is.Zero);

        var incompatibleNativeProducer = NativeModuleCompiler.CreateProducer(
            incompatibleCompilation.Program!, exports, "native_vtable", NativeRuntimePolicy.FailFast);
        Assert.Throws<ModuleContractException>(() =>
            NativeModuleCompiler.ValidateConsumerSet(nativeSet,
                [incompatibleNativeProducer, extraNativeProducer]));
        Assert.Throws<ModuleContractException>(() =>
            NativeModuleCompiler.ValidateGraph(graphConsumer,
                [incompatibleNativeProducer, extraNativeProducer]));

        var directCompilation = Compilation.Create(SyntaxTree.Parse(providerSource + consumerSource));
        Assert.That(directCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, directCompilation.Diagnostics));
        Assert.Throws<NotSupportedException>(() =>
            TrRmExecutableCompiler.Emit(directCompilation.Program!, "native_vtable"));
    }
}
