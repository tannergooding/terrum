// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Terrum.CodeAnalysis.Tests.Infrastructure;
using Terrum.CodeAnalysis.Tests.Semantics;

namespace Terrum.CodeAnalysis.Tests.Native.Interop;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "NUnit instantiates this fixture through reflection.")]
internal sealed class PrivateForeignNativeInterfaceTests
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NativeFniTryRead(IntPtr receiver, IntPtr output);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NativeFniDispatch(IntPtr receiver);

    [Test]
    public async Task PrivateFniNativeOutputDispatchPublishesReturnedAggregateAsync()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        const string schemaIdentity
            = "terrum/private-semantic-111-foreign-native-interfaces-1";
        const string providerIdentity = "PrivateFniRuntimeProvider";
        const string ownerKey = "abi.iprobe";
        const string memberKey = "abi.iprobe.try-read";
        var providerSource = SyntaxTree.Parse("""
            using Terrum.Interop;
            public int32 Identity(int32 value) { return value; }
            [NativeCallable(CallConv: NativeCallingConvention.Cdecl)]
            [ForeignNativeInterface(VTableByteOffset: uint32(0))]
            public interface IProbe {
                [param(value): NativeOutput(
                    When: NativeOutputCondition.NonZero)]
                public unsafe int32 TryRead(write ptr<int32> value);
            }
            """);
        var (sourceNativeRegistration, sourceOutputRegistration)
            = NativeOutputCoreRegistrationTests
                .CreateCombinedRegistrations(
                    includeForeignNativeInterface: true);
        var moduleBindings = CoreProviderModuleBindings.Create(
            providerIdentity,
            sourceNativeRegistration.ModuleBindings.Providers,
            sourceNativeRegistration.ModuleBindings.Declarations);
        var nativeRegistration = NativeCoreRegistrationSet.Create(
            moduleBindings, sourceNativeRegistration.CallingConventions,
            sourceNativeRegistration.ResolutionModes,
            sourceNativeRegistration.ResolutionPolicies,
            sourceNativeRegistration.NativeImport,
            sourceNativeRegistration.NativeCallable,
            sourceNativeRegistration.NativeCallConv,
            sourceNativeRegistration.NativeExport,
            sourceNativeRegistration.ForeignNativeInterface);
        var nativeOutputRegistration
            = NativeOutputCoreRegistrationSet.Create(moduleBindings,
                sourceOutputRegistration.OutputConditions,
                sourceOutputRegistration.NativeOutput);
        var sourceLayoutRegistration
            = NativeCoreRegistrationTests.CreateLayoutRegistration(
                sourceNativeRegistration);
        var layoutRegistration = LayoutCoreRegistrationSet.Create(
            moduleBindings, sourceLayoutRegistration.LayoutKinds,
            sourceLayoutRegistration.Layout,
            sourceLayoutRegistration.Blittable);
        var options = new CompilationOptions {
            NativeCoreRegistration = nativeRegistration,
            LayoutCoreRegistration = layoutRegistration,
            NativeOutputCoreRegistration =
                nativeOutputRegistration,
        };
        var providerCompilation
            = Compilation.CreateLibrary(providerSource, options);
        Assert.That(providerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                providerCompilation.Diagnostics));
        var providerProgram = providerCompilation.Program!;
        var providerInputs = PrivateModuleIdentityInputs
            .CreateFromSelectors(providerProgram, providerIdentity, [], [],
                [], [], [
                    new PrivateForeignInterfaceDefinitionAssignment(0,
                        ownerKey, true, [
                            new PrivateForeignInterfaceMemberDefinitionAssignment(
                                0, memberKey),
                        ]),
                ], []);
        var producer = ModuleCompiler.CreateProducerWithSchema(
            providerProgram, ["Identity"], schemaIdentity, providerInputs);
        var reference = ModuleCompiler.CreateReferenceWithSchema(producer,
            schemaIdentity);
        var consumerSource = SyntaxTree.Parse("""
            int32 Main() { return 0; }
            public unsafe int32 Dispatch(ptr<void> receiver) {
                init int32 value;
                unsafe {
                    int32 status = IProbe.TryRead(receiver, &init value);
                    if (status != 0) { return value; }
                }
                return 0;
            }
            """);
        var consumerCompilation = Compilation.Create(consumerSource,
            reference, new CompilationOptions {
                RuntimePolicy = BoundRuntimePolicy.FailFast,
            });
        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                consumerCompilation.Diagnostics));
        var consumerProgram = consumerCompilation.Program!;
        var consumerInputs = PrivateModuleIdentityInputs
            .CreateFromSelectors(consumerProgram,
                "PrivateFniRuntimeConsumer", [], [], [], [], [], [
                    new PrivateForeignInterfaceImportAssignment(0,
                        "consumer.iprobe", providerIdentity, ownerKey, [
                            new PrivateForeignInterfaceMemberImportAssignment(
                                0, "consumer.iprobe.try-read", memberKey),
                        ]),
                ]);
        var consumer = ModuleCompiler.CreateConsumerWithSchema(
            consumerProgram, producer, schemaIdentity, consumerInputs);
        var linked = ModuleCompiler.Link(consumer, producer);
        var libraryProgram = linked with { EntryPoint = -1 };

        var directory = TestArtifactPaths.CreateDirectory(
            "terrum-private-fni-runtime-");
        var libraryPath = Path.Combine(directory, "consumer.dll");
        var library = IntPtr.Zero;
        var vtable = IntPtr.Zero;
        var receiver = IntPtr.Zero;
        try
        {
            await File.WriteAllBytesAsync(libraryPath,
                NativeEmitter.EmitLibrary(libraryProgram, ["Dispatch"],
                    NativeRuntimePolicy.FailFast)).ConfigureAwait(true);
            library = NativeLibrary.Load(libraryPath);
            vtable = Marshal.AllocHGlobal(IntPtr.Size);
            receiver = Marshal.AllocHGlobal(IntPtr.Size);
            var callback = new NativeFniTryRead(PublishOutput);
            Marshal.WriteIntPtr(vtable,
                Marshal.GetFunctionPointerForDelegate(callback));
            Marshal.WriteIntPtr(receiver, vtable);
            var dispatch = Marshal.GetDelegateForFunctionPointer<
                NativeFniDispatch>(NativeLibrary.GetExport(library,
                    "Dispatch"));
            Assert.That(dispatch(receiver), Is.EqualTo(42));
            GC.KeepAlive(callback);
        }
        finally
        {
            if (library != IntPtr.Zero)
            {
                NativeLibrary.Free(library);
            }
            if (receiver != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(receiver);
            }
            if (vtable != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(vtable);
            }
            await TestFileCleanup.DeleteDirectoryWithRetryAsync(directory)
                .ConfigureAwait(true);
        }
    }

    [Test]
    public void PrivateFniGraphProducerRetainsSemanticOwnerWithoutReexportingIt()
    {
        const string schemaIdentity
            = "terrum/private-semantic-111-foreign-native-interfaces-1";
        const string graphIdentity = "PrivateFniGraph";
        const string ownerKey = "graph.iprobe";
        const string memberKey = "graph.iprobe.get";

        var dependencyCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(
                "public int32 Add(int32 value) { return value + 1; }"));
        Assert.That(dependencyCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                dependencyCompilation.Diagnostics));
        var dependency = NativeModuleCompiler.CreateProducerWithSchema(
            dependencyCompilation.Program!, ["Add"], "PrivateFniLeaf",
            schemaIdentity, NativeRuntimePolicy.FailFast);
        var dependencyReference
            = NativeModuleCompiler.CreateReferenceWithSchema(dependency,
                schemaIdentity);

        var source = SyntaxTree.Parse("""
            using Terrum.Interop;
            public int32 Forward(ptr<void> receiver, int32 value) {
                unsafe { return IProbe.Get(receiver, Add(value)); }
            }
            [NativeCallable(CallConv: NativeCallingConvention.Cdecl)]
            [ForeignNativeInterface(VTableByteOffset: uint32(0))]
            public interface IProbe {
                public unsafe int32 Get(int32 value);
            }
            """);
        var sourceNativeRegistration
            = NativeCoreRegistrationTests.CreateRegistration(
                includeForeignNativeInterface: true);
        var graphBindings = CoreProviderModuleBindings.Create(
            graphIdentity,
            sourceNativeRegistration.ModuleBindings.Providers,
            sourceNativeRegistration.ModuleBindings.Declarations);
        var nativeRegistration = NativeCoreRegistrationSet.Create(
            graphBindings, sourceNativeRegistration.CallingConventions,
            sourceNativeRegistration.ResolutionModes,
            sourceNativeRegistration.ResolutionPolicies,
            sourceNativeRegistration.NativeImport,
            sourceNativeRegistration.NativeCallable,
            sourceNativeRegistration.NativeCallConv,
            sourceNativeRegistration.NativeExport,
            sourceNativeRegistration.ForeignNativeInterface);
        var compilation = Compilation.CreateLibrary(source,
            dependencyReference, new CompilationOptions {
                NativeCoreRegistration = nativeRegistration,
                RuntimePolicy = BoundRuntimePolicy.FailFast,
            });
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program!;
        var identityInputs = PrivateModuleIdentityInputs.CreateFromSelectors(
            program, graphIdentity, [], [], [], [], [
                new PrivateForeignInterfaceDefinitionAssignment(0,
                    ownerKey, false, [
                        new PrivateForeignInterfaceMemberDefinitionAssignment(
                            0, memberKey),
                    ]),
            ], []);
        var graph = NativeModuleCompiler.CreateGraphProducerWithSchema(
            program, ["Forward"], graphIdentity, [dependency],
            schemaIdentity, NativeRuntimePolicy.FailFast, identityInputs);
        var reference = NativeModuleCompiler.CreateGraphReferenceWithSchema(
            [graph, dependency], schemaIdentity);

        Assert.That(reference.Functions.Select(static function => function.Name),
            Is.EquivalentTo(["Add", "Forward"]));
        Assert.That(reference.Interfaces, Is.Empty);
    }

    [Test]
    public void PrivateFniReferenceSetsPreserveDistinctOwnerBindings()
    {
        const string schemaIdentity
            = "terrum/private-semantic-111-foreign-native-interfaces-1";
        var first = CreateFniProvider("PrivateFniSetA", "IProbeA", "AddA",
            "PayloadA");
        var second = CreateFniProvider("PrivateFniSetB", "IProbeB", "AddB",
            "PayloadB");
        var reference = ModuleCompiler.CreateReferenceSetWithSchema(
            [first, second], schemaIdentity);
        var source = SyntaxTree.Parse("""
            public unsafe int32 CallA(ptr<void> receiver,
                ptr<PayloadA> value) {
                unsafe { return IProbeA.Get(receiver, value); }
            }
            public unsafe int32 CallB(ptr<void> receiver,
                ptr<PayloadB> value) {
                unsafe { return IProbeB.Get(receiver, value); }
            }
            """);
        var compilation = Compilation.CreateLibrary(source, reference);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var reversedReference = ModuleCompiler.CreateReferenceSetWithSchema(
            [second, first], schemaIdentity);
        var reversedCompilation = Compilation.CreateLibrary(source,
            reversedReference);
        Assert.That(reversedCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                reversedCompilation.Diagnostics));
    }

    [Test]
    public void PrivateFniDirectoryRejectsMemberRedirectedToOrdinaryFunction()
    {
        const string schemaIdentity
            = "terrum/private-semantic-111-foreign-native-interfaces-1";
        const string moduleIdentity = "PrivateFniDirectoryTarget";
        var producer = CreateFniProvider(moduleIdentity, "IProbe", "Add",
            "Payload");
        _ = ModuleCompiler.CreateReferenceWithSchema(producer,
            schemaIdentity);
        var memberKeyBytes = Encoding.UTF8.GetBytes(
            moduleIdentity + ".member");
        var memberKeyOffset = producer.AsSpan().IndexOf(memberKeyBytes);
        Assert.That(memberKeyOffset, Is.GreaterThanOrEqualTo(0));

        var malformed = (byte[])producer.Clone();
        BinaryPrimitives.WriteUInt32LittleEndian(malformed.AsSpan(
            memberKeyOffset + memberKeyBytes.Length), 0);
        _ = Assert.Throws<ModuleFormatException>(() =>
            ModuleCompiler.CreateReferenceWithSchema(malformed,
                schemaIdentity));
    }

    [Test]
    public void PrivateFniCallRejectsTargetOutsideTheOwnerDirectory()
    {
        const string schemaIdentity
            = "terrum/private-semantic-111-foreign-native-interfaces-1";
        const string providerIdentity = "PrivateFniCallProvider";
        const string callText = "IProbe.Get(receiver, value)";
        var producer = CreateFniProvider(providerIdentity, "IProbe", "Add",
            "Payload");
        var reference = ModuleCompiler.CreateReferenceWithSchema(producer,
            schemaIdentity);
        const string consumerSource = """
            int32 Invoke(ptr<void> receiver, ptr<Payload> value) {
                unsafe { return IProbe.Get(receiver, value); }
            }
            """;
        var compilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(consumerSource), reference);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program!;
        var identities = PrivateModuleIdentityInputs.CreateFromSelectors(
            program, "PrivateFniCallConsumer", [], [], [], [], [], [
                new PrivateForeignInterfaceImportAssignment(0,
                    "consumer.iprobe", providerIdentity,
                    providerIdentity + ".owner", [
                        new PrivateForeignInterfaceMemberImportAssignment(
                            0, "consumer.iprobe.get",
                            providerIdentity + ".member"),
                    ]),
            ]);
        var consumer = ModuleCompiler.CreateConsumerWithSchema(program,
            producer, schemaIdentity, identities);
        var callOffset = FindExpressionOffset(consumer, 5,
            (int)PrimitiveType.Int32,
            consumerSource.IndexOf(callText, StringComparison.Ordinal),
            callText.Length);
        Assert.That(callOffset, Is.GreaterThanOrEqualTo(0));
        Assert.That(consumer[callOffset + 13], Is.EqualTo(1));

        var malformed = (byte[])consumer.Clone();
        BinaryPrimitives.WriteUInt32LittleEndian(
            malformed.AsSpan(callOffset + 14), 0);
        _ = Assert.Throws<ModuleFormatException>(() =>
            ModuleCompiler.Link(malformed, producer));
    }

    [Test]
    public void PrivateFniCallRejectsDuplicateFormalMappings()
    {
        const string schemaIdentity
            = "terrum/private-semantic-111-foreign-native-interfaces-1";
        const string providerIdentity = "PrivateFniMapProvider";
        const string callText
            = "IProbe.Get(receiver, second: second, first: first)";
        var producer = CreateFniProvider(providerIdentity, "IProbe", "Add",
            "Payload", useTwoScalarFormals: true);
        var reference = ModuleCompiler.CreateReferenceWithSchema(producer,
            schemaIdentity);
        const string consumerSource = """
            int32 Invoke(ptr<void> receiver, int32 first, int32 second) {
                unsafe { return IProbe.Get(receiver, second: second, first: first); }
            }
            """;
        var compilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(consumerSource), reference);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program!;
        var identities = PrivateModuleIdentityInputs.CreateFromSelectors(
            program, "PrivateFniMapConsumer", [], [], [], [], [], [
                new PrivateForeignInterfaceImportAssignment(0,
                    "consumer.iprobe", providerIdentity,
                    providerIdentity + ".owner", [
                        new PrivateForeignInterfaceMemberImportAssignment(
                            0, "consumer.iprobe.get",
                            providerIdentity + ".member"),
                    ]),
            ]);
        var consumer = ModuleCompiler.CreateConsumerWithSchema(program,
            producer, schemaIdentity, identities);
        var callOffset = FindExpressionOffset(consumer, 5,
            (int)PrimitiveType.Int32,
            consumerSource.IndexOf(callText, StringComparison.Ordinal),
            callText.Length);
        Assert.That(callOffset, Is.GreaterThanOrEqualTo(0));
        Assert.That(consumer[callOffset + 22], Is.EqualTo(1));
        var firstFormal = BinaryPrimitives.ReadInt32LittleEndian(
            consumer.AsSpan(callOffset + 23, sizeof(int)));

        var malformed = (byte[])consumer.Clone();
        BinaryPrimitives.WriteInt32LittleEndian(
            malformed.AsSpan(callOffset + 27, sizeof(int)), firstFormal);
        _ = Assert.Throws<ModuleFormatException>(() =>
            ModuleCompiler.Link(malformed, producer));
    }

    [Test]
    public void PrivateFniCallRejectsInvalidOutputReservationLocal()
    {
        const string schemaIdentity
            = "terrum/private-semantic-111-foreign-native-interfaces-1";
        const string providerIdentity = "PrivateFniReservationProvider";
        var providerSource = SyntaxTree.Parse("""
            using Terrum.Interop;
            public int32 Add(int32 value) { return value + 1; }
            [NativeCallable(CallConv: NativeCallingConvention.Cdecl)]
            [ForeignNativeInterface(VTableByteOffset: uint32(0))]
            public interface IProbe {
                [param(value): NativeOutput(
                    When: NativeOutputCondition.NonZero)]
                public unsafe int32 TryRead(write ptr<int32> value);
            }
            """);
        var (sourceNativeRegistration, sourceOutputRegistration)
            = NativeOutputCoreRegistrationTests
                .CreateCombinedRegistrations(
                    includeForeignNativeInterface: true);
        var moduleBindings = CoreProviderModuleBindings.Create(
            providerIdentity,
            sourceNativeRegistration.ModuleBindings.Providers,
            sourceNativeRegistration.ModuleBindings.Declarations);
        var nativeRegistration = NativeCoreRegistrationSet.Create(
            moduleBindings, sourceNativeRegistration.CallingConventions,
            sourceNativeRegistration.ResolutionModes,
            sourceNativeRegistration.ResolutionPolicies,
            sourceNativeRegistration.NativeImport,
            sourceNativeRegistration.NativeCallable,
            sourceNativeRegistration.NativeCallConv,
            sourceNativeRegistration.NativeExport,
            sourceNativeRegistration.ForeignNativeInterface);
        var outputRegistration = NativeOutputCoreRegistrationSet.Create(
            moduleBindings, sourceOutputRegistration.OutputConditions,
            sourceOutputRegistration.NativeOutput);
        var providerCompilation = Compilation.CreateLibrary(providerSource,
            new CompilationOptions {
                NativeCoreRegistration = nativeRegistration,
                LayoutCoreRegistration =
                    NativeCoreRegistrationTests.CreateLayoutRegistration(
                        nativeRegistration),
                NativeOutputCoreRegistration = outputRegistration,
            });
        Assert.That(providerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                providerCompilation.Diagnostics));
        var providerProgram = providerCompilation.Program!;
        var providerInputs = PrivateModuleIdentityInputs.CreateFromSelectors(
            providerProgram, providerIdentity, [], [], [], [], [
                new PrivateForeignInterfaceDefinitionAssignment(0,
                    providerIdentity + ".owner", true, [
                        new PrivateForeignInterfaceMemberDefinitionAssignment(
                            0, providerIdentity + ".member"),
                    ]),
            ], []);
        var producer = ModuleCompiler.CreateProducerWithSchema(
            providerProgram, ["Add"], schemaIdentity, providerInputs);
        var reference = ModuleCompiler.CreateReferenceWithSchema(producer,
            schemaIdentity);
        const string consumerSource = """
            int32 Invoke(ptr<void> receiver) {
                init int32 value;
                unsafe {
                    int32 status = IProbe.TryRead(receiver, &init value);
                    if (status != 0) { return value; }
                }
                return 0;
            }
            """;
        var consumerCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(consumerSource), reference);
        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                consumerCompilation.Diagnostics));
        var consumerProgram = consumerCompilation.Program!;
        var consumerInputs = PrivateModuleIdentityInputs
            .CreateFromSelectors(consumerProgram,
                "PrivateFniReservationConsumer", [], [], [], [], [], [
                    new PrivateForeignInterfaceImportAssignment(0,
                        "consumer.iprobe", providerIdentity,
                        providerIdentity + ".owner", [
                            new PrivateForeignInterfaceMemberImportAssignment(
                                0, "consumer.iprobe.try-read",
                                providerIdentity + ".member"),
                        ]),
                ]);
        var consumer = ModuleCompiler.CreateConsumerWithSchema(
            consumerProgram, producer, schemaIdentity, consumerInputs);
        var reservationOffset = FindExpressionOffset(consumer, 41,
            (int)PrimitiveType.Void, -1, -1);
        Assert.That(reservationOffset, Is.GreaterThanOrEqualTo(0));

        var malformed = (byte[])consumer.Clone();
        BinaryPrimitives.WriteInt32LittleEndian(
            malformed.AsSpan(reservationOffset + 13, sizeof(int)),
            int.MaxValue);
        _ = Assert.Throws<ModuleFormatException>(() =>
            ModuleCompiler.Link(malformed, producer));
    }

    private static byte[] CreateFniProvider(string moduleIdentity,
        string interfaceName, string functionName, string payloadTypeName,
        bool useTwoScalarFormals = false)
    {
        const string schemaIdentity
            = "terrum/private-semantic-111-foreign-native-interfaces-1";
        var formalParameters = useTwoScalarFormals
            ? "int32 first, int32 second"
            : $"ptr<{payloadTypeName}> value";
        var source = SyntaxTree.Parse($$"""
            using Terrum.Interop;
            public int32 {{functionName}}(int32 value) { return value; }
            [Blittable]
            public struct {{payloadTypeName}} {
                public int32 Value;
            }
            [NativeCallable(CallConv: NativeCallingConvention.Cdecl)]
            [ForeignNativeInterface(VTableByteOffset: uint32(0))]
            public interface {{interfaceName}} {
                public unsafe int32 Get({{formalParameters}});
            }
            """);
        var nativeRegistration
            = CreateFniNativeCoreRegistration(moduleIdentity);
        var compilation = Compilation.CreateLibrary(source,
            new CompilationOptions {
                NativeCoreRegistration = nativeRegistration,
                LayoutCoreRegistration =
                    NativeCoreRegistrationTests
                        .CreateLayoutRegistration(nativeRegistration),
                RuntimePolicy = BoundRuntimePolicy.FailFast,
            });
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program!;
        var identities = PrivateModuleIdentityInputs.CreateFromSelectors(
            program, moduleIdentity, [
                new PrivateNominalDefinitionSelection(
                    new PrivateNominalDeclarationSelector(
                        PrivateNominalDeclarationKind.Struct,
                        payloadTypeName),
                    moduleIdentity + ".payload", true),
            ], [], [], [], [
                new PrivateForeignInterfaceDefinitionAssignment(0,
                    moduleIdentity + ".owner", true, [
                        new PrivateForeignInterfaceMemberDefinitionAssignment(
                            0, moduleIdentity + ".member"),
                    ]),
            ], []);
        return ModuleCompiler.CreateProducerWithSchema(program,
            [functionName], schemaIdentity, identities);
    }

    private static NativeCoreRegistrationSet
        CreateFniNativeCoreRegistration(string usingModuleIdentity)
    {
        var source = NativeCoreRegistrationTests.CreateRegistration(
            includeForeignNativeInterface: true);
        var bindings = CoreProviderModuleBindings.Create(
            usingModuleIdentity, source.ModuleBindings.Providers,
            source.ModuleBindings.Declarations);
        return NativeCoreRegistrationSet.Create(bindings,
            source.CallingConventions, source.ResolutionModes,
            source.ResolutionPolicies, source.NativeImport,
            source.NativeCallable, source.NativeCallConv,
            source.NativeExport, source.ForeignNativeInterface);
    }

    private static int FindExpressionOffset(byte[] module, byte kind,
        int type, int spanStart, int spanLength)
    {
        for (var offset = 0; offset <= module.Length - 13; offset++)
        {
            if (module[offset] == kind
                && BinaryPrimitives.ReadInt32LittleEndian(
                    module.AsSpan(offset + 1, sizeof(int))) == type
                && (spanStart < 0
                    || BinaryPrimitives.ReadInt32LittleEndian(
                        module.AsSpan(offset + 5, sizeof(int))) == spanStart)
                && (spanLength < 0
                    || BinaryPrimitives.ReadInt32LittleEndian(
                        module.AsSpan(offset + 9, sizeof(int)))
                        == spanLength))
            {
                return offset;
            }
        }
        return -1;
    }

    private static int PublishOutput(IntPtr receiver, IntPtr output)
    {
        _ = receiver;
        Marshal.WriteInt32(output, 42);
        return 1;
    }
}
