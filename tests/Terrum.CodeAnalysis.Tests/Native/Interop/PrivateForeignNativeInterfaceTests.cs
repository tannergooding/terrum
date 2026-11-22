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
            [Blittable] [Layout(LayoutKind.Sequential)]
            public struct Payload { public int32 Value; }
            [NativeCallable(CallConv: NativeCallingConvention.Cdecl)]
            [ForeignNativeInterface(VTableByteOffset: uint32(0))]
            public interface IProbe {
                [param(value): NativeOutput(
                    When: NativeOutputCondition.NonZero)]
                public unsafe int32 TryRead(write ptr<Payload> value);
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
            .CreateFromSelectors(providerProgram, providerIdentity, [
                new PrivateNominalDefinitionSelection(
                    new PrivateNominalDeclarationSelector(
                        PrivateNominalDeclarationKind.Struct, "Payload"),
                    "abi.payload", true),
            ], [], [], [], [
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
                init Payload value;
                unsafe {
                    int32 status = IProbe.TryRead(receiver, &init value);
                    if (status != 0) { return value.Value; }
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
                "PrivateFniRuntimeConsumer", [], [
                    new PrivateNominalImportSelection(
                        new PrivateNominalDeclarationSelector(
                            PrivateNominalDeclarationKind.Struct,
                            "Payload"),
                        "consumer.payload", providerIdentity,
                        "abi.payload"),
                ], [], [], [], [
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
    public void PrivateFniGraphRetainsImportedOwnerWithoutReexportingIt()
    {
        const string schemaIdentity
            = "terrum/private-semantic-111-foreign-native-interfaces-1";
        const string leafIdentity = "PrivateFniGraphLeaf";
        const string graphIdentity = "PrivateFniGraphWrapper";
        const string ownerKey = "leaf.iprobe";
        const string memberKey = "leaf.iprobe.get";
        var leafSource = SyntaxTree.Parse("""
            using Terrum.Interop;
            public int32 Identity(int32 value) { return value; }
            [NativeCallable(CallConv: NativeCallingConvention.Cdecl)]
            [ForeignNativeInterface(VTableByteOffset: uint32(0))]
            public interface IProbe {
                public unsafe int32 Get(int32 value);
            }
            """);
        var leafCompilation = Compilation.CreateLibrary(leafSource,
            new CompilationOptions {
                NativeCoreRegistration =
                    CreateFniNativeCoreRegistration(leafIdentity),
            });
        Assert.That(leafCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, leafCompilation.Diagnostics));
        var leafProgram = leafCompilation.Program!;
        var leafInputs = PrivateModuleIdentityInputs.CreateFromSelectors(
            leafProgram, leafIdentity, [], [], [], [], [
                new PrivateForeignInterfaceDefinitionAssignment(0,
                    ownerKey, true, [
                        new PrivateForeignInterfaceMemberDefinitionAssignment(
                            0, memberKey),
                    ]),
            ], []);
        var leafPackage = NativeModuleCompiler.CreateProducerWithSchema(
            leafProgram, ["Identity"], leafIdentity, schemaIdentity,
            NativeRuntimePolicy.FailFast, leafInputs);
        var leafReference = NativeModuleCompiler
            .CreateReferenceWithSchema(leafPackage, schemaIdentity);

        var graphSource = SyntaxTree.Parse("""
            public int32 Forward(ptr<void> receiver, int32 value) {
                unsafe { return IProbe.Get(receiver, value); }
            }
            """);
        var graphCompilation = Compilation.CreateLibrary(graphSource,
            leafReference);
        Assert.That(graphCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                graphCompilation.Diagnostics));
        var graphProgram = graphCompilation.Program!;
        var graphInputs = PrivateModuleIdentityInputs.CreateFromSelectors(
            graphProgram, graphIdentity, [], [], [], [], [], [
                new PrivateForeignInterfaceImportAssignment(0,
                    "graph.iprobe", leafIdentity, ownerKey, [
                        new PrivateForeignInterfaceMemberImportAssignment(
                            0, "graph.iprobe.get", memberKey),
                    ]),
            ]);
        var graphPackage = NativeModuleCompiler
            .CreateGraphProducerWithSchema(graphProgram, ["Forward"],
                graphIdentity, [leafPackage], schemaIdentity,
                NativeRuntimePolicy.FailFast, graphInputs);
        var graphOnlyReference = NativeModuleCompiler
            .CreateGraphReferenceWithSchema([graphPackage], schemaIdentity);
        Assert.That(graphOnlyReference.Functions.Select(
                static function => function.Name),
            Is.EquivalentTo(["Forward"]));
        Assert.That(graphOnlyReference.Interfaces, Is.Empty);
        var reference = NativeModuleCompiler.CreateGraphReferenceWithSchema(
            [graphPackage, leafPackage], schemaIdentity);

        Assert.That(reference.Functions.Select(static function =>
                function.Name),
            Is.EquivalentTo(["Forward", "Identity"]));
        Assert.That(reference.Interfaces.Select(static definition =>
                definition.QualifiedName),
            Is.EqualTo(["IProbe"]));
        var consumerCompilation = Compilation.Create(SyntaxTree.Parse(
            """
            int32 Main(ptr<void> receiver) {
                unsafe { return Forward(receiver, 41); }
            }
            """),
            graphOnlyReference);
        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                consumerCompilation.Diagnostics));
        var consumer = NativeModuleCompiler.CreateGraphConsumerWithSchema(
            consumerCompilation.Program!, [graphPackage, leafPackage],
            schemaIdentity, NativeRuntimePolicy.FailFast);
        NativeModuleCompiler.ValidateGraph(consumer,
            [graphPackage, leafPackage]);
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
    public void PrivateFniReferenceSetsRejectConflictingOwnerIdentity()
    {
        const string schemaIdentity
            = "terrum/private-semantic-111-foreign-native-interfaces-1";
        const string moduleIdentity = "PrivateFniSetConflict";
        var first = CreateFniProvider(moduleIdentity, "IProbe", "AddA",
            "Payload");
        var conflicting = CreateFniProvider(moduleIdentity, "IProbe", "AddB",
            "Payload", useTwoScalarFormals: true);

        _ = Assert.Throws<ModuleContractException>(() =>
            ModuleCompiler.CreateReferenceSetWithSchema([first, conflicting],
                schemaIdentity));
    }

    [Test]
    public void PrivateFniAggregateProviderBindsAcrossTypeDefRelocation()
    {
        const string schemaIdentity
            = "terrum/private-semantic-111-foreign-native-interfaces-1";
        const string providerIdentity = "PrivateFniTypeRelocation";
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
            program, "PrivateFniTypeRelocationConsumer", [], [
                new PrivateNominalImportSelection(
                    new PrivateNominalDeclarationSelector(
                        PrivateNominalDeclarationKind.Struct, "Payload"),
                    "consumer.payload", providerIdentity,
                    providerIdentity + ".payload"),
            ], [], [], [], [
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
        var relocatedProducer = CreateFniProvider(providerIdentity, "IProbe",
            "Add", "Payload", prependPrefixStruct: true);
        var relocatedReference = ModuleCompiler.CreateReferenceWithSchema(
            relocatedProducer, schemaIdentity);
        Assert.That(reference.Structures.Select(static structure =>
                structure.Name),
            Is.EqualTo(["Payload"]));
        Assert.That(relocatedReference.Structures.Select(
                static structure => structure.Name),
            Is.EqualTo(["Prefix", "Payload"]));

        var linked = ModuleCompiler.Link(consumer, relocatedProducer);
        Assert.That(linked.Functions.Select(static function => function.Name),
            Does.Contain("Invoke"));

        var incompatibleProducer = CreateFniProvider(providerIdentity,
            "IProbe", "Add", "Payload", payloadFieldType: "int64",
            prependPrefixStruct: true);
        _ = Assert.Throws<ModuleContractException>(() =>
            ModuleCompiler.Link(consumer, incompatibleProducer));
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
        malformed = (byte[])producer.Clone();
        BinaryPrimitives.WriteUInt32LittleEndian(malformed.AsSpan(
            memberKeyOffset + memberKeyBytes.Length), uint.MaxValue);
        _ = Assert.Throws<ModuleFormatException>(() =>
            ModuleCompiler.CreateReferenceWithSchema(malformed,
                schemaIdentity));
    }

    [Test]
    public void PrivateFniDirectoryRejectsReversedMemberFunctionReferences()
    {
        const string schemaIdentity
            = "terrum/private-semantic-111-foreign-native-interfaces-1";
        const string moduleIdentity = "PrivateFniReverseDirectory";
        var producer = CreateTwoMemberFniProvider(moduleIdentity);
        _ = ModuleCompiler.CreateReferenceWithSchema(producer,
            schemaIdentity);
        var firstKey = Encoding.UTF8.GetBytes(moduleIdentity + ".get");
        var secondKey = Encoding.UTF8.GetBytes(moduleIdentity + ".read");
        var firstKeyOffset = producer.AsSpan().IndexOf(firstKey);
        var secondKeyOffset = producer.AsSpan().IndexOf(secondKey);
        Assert.That(firstKeyOffset, Is.GreaterThanOrEqualTo(0));
        Assert.That(secondKeyOffset, Is.GreaterThanOrEqualTo(0));
        var firstTargetOffset = firstKeyOffset + firstKey.Length;
        var secondTargetOffset = secondKeyOffset + secondKey.Length;
        var firstTarget = BinaryPrimitives.ReadUInt32LittleEndian(
            producer.AsSpan(firstTargetOffset, sizeof(uint)));
        var secondTarget = BinaryPrimitives.ReadUInt32LittleEndian(
            producer.AsSpan(secondTargetOffset, sizeof(uint)));
        Assert.That(firstTarget, Is.Not.EqualTo(secondTarget));

        var malformed = (byte[])producer.Clone();
        BinaryPrimitives.WriteUInt32LittleEndian(
            malformed.AsSpan(firstTargetOffset, sizeof(uint)), secondTarget);
        BinaryPrimitives.WriteUInt32LittleEndian(
            malformed.AsSpan(secondTargetOffset, sizeof(uint)), firstTarget);
        _ = Assert.Throws<ModuleFormatException>(() =>
            ModuleCompiler.CreateReferenceWithSchema(malformed,
                schemaIdentity));
    }

    [Test]
    public void PrivateFniDirectoryRejectsOwnerCountMismatch()
    {
        const string schemaIdentity
            = "terrum/private-semantic-111-foreign-native-interfaces-1";
        const string moduleIdentity = "PrivateFniOwnerCount";
        var producer = CreateTwoMemberFniProvider(moduleIdentity);
        _ = ModuleCompiler.CreateReferenceWithSchema(producer,
            schemaIdentity);
        var ownerKey = Encoding.UTF8.GetBytes(moduleIdentity + ".owner");
        var ownerKeyOffset = producer.AsSpan().IndexOf(ownerKey);
        Assert.That(ownerKeyOffset, Is.GreaterThanOrEqualTo(0));
        var directoryCountOffset = ownerKeyOffset - sizeof(int)
            - sizeof(int) - Encoding.UTF8.GetByteCount(moduleIdentity)
            - sizeof(byte) - sizeof(bool) - sizeof(ushort) - sizeof(int);
        Assert.That(directoryCountOffset, Is.GreaterThanOrEqualTo(0));
        Assert.That(BinaryPrimitives.ReadInt32LittleEndian(
            producer.AsSpan(directoryCountOffset, sizeof(int))),
            Is.EqualTo(1));

        var malformed = (byte[])producer.Clone();
        BinaryPrimitives.WriteInt32LittleEndian(
            malformed.AsSpan(directoryCountOffset, sizeof(int)), 2);
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
        malformed = (byte[])consumer.Clone();
        BinaryPrimitives.WriteUInt32LittleEndian(
            malformed.AsSpan(callOffset + 14), uint.MaxValue);
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
        malformed = (byte[])consumer.Clone();
        BinaryPrimitives.WriteInt32LittleEndian(
            malformed.AsSpan(callOffset + 27, sizeof(int)), int.MaxValue);
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
        bool useTwoScalarFormals = false, string payloadFieldType = "int32",
        bool prependPrefixStruct = false)
    {
        const string schemaIdentity
            = "terrum/private-semantic-111-foreign-native-interfaces-1";
        var formalParameters = useTwoScalarFormals
            ? "int32 first, int32 second"
            : $"ptr<{payloadTypeName}> value";
        var prefixDeclaration = prependPrefixStruct
            ? """
              [Blittable] [Layout(LayoutKind.Sequential)]
              public struct Prefix { public int32 Value; }
              """
            : string.Empty;
        var source = SyntaxTree.Parse($$"""
            using Terrum.Interop;
            public int32 {{functionName}}(int32 value) { return value; }
            {{prefixDeclaration}}
            [Blittable]
            public struct {{payloadTypeName}} {
                public {{payloadFieldType}} Value;
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
        var payloadDefinition = new PrivateNominalDefinitionSelection(
            new PrivateNominalDeclarationSelector(
                PrivateNominalDeclarationKind.Struct, payloadTypeName),
            moduleIdentity + ".payload", true);
        var nominalDefinitions = prependPrefixStruct
            ? new[] {
                new PrivateNominalDefinitionSelection(
                    new PrivateNominalDeclarationSelector(
                        PrivateNominalDeclarationKind.Struct, "Prefix"),
                    moduleIdentity + ".prefix", true),
                payloadDefinition,
            }
            : new[] { payloadDefinition };
        var identities = PrivateModuleIdentityInputs.CreateFromSelectors(
            program, moduleIdentity, nominalDefinitions, [], [], [], [
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

    private static byte[] CreateTwoMemberFniProvider(
        string moduleIdentity)
    {
        const string schemaIdentity
            = "terrum/private-semantic-111-foreign-native-interfaces-1";
        var source = SyntaxTree.Parse("""
            using Terrum.Interop;
            public int32 Identity(int32 value) { return value; }
            [NativeCallable(CallConv: NativeCallingConvention.Cdecl)]
            [ForeignNativeInterface(VTableByteOffset: uint32(0))]
            public interface IProbe {
                public unsafe int32 Get(int32 value);
                public unsafe int32 Read(int64 value);
            }
            """);
        var compilation = Compilation.CreateLibrary(source,
            new CompilationOptions {
                NativeCoreRegistration =
                    CreateFniNativeCoreRegistration(moduleIdentity),
            });
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program!;
        var identities = PrivateModuleIdentityInputs.CreateFromSelectors(
            program, moduleIdentity, [], [], [], [], [
                new PrivateForeignInterfaceDefinitionAssignment(0,
                    moduleIdentity + ".owner", true, [
                        new PrivateForeignInterfaceMemberDefinitionAssignment(
                            0, moduleIdentity + ".get"),
                        new PrivateForeignInterfaceMemberDefinitionAssignment(
                            1, moduleIdentity + ".read"),
                    ]),
            ], []);
        return ModuleCompiler.CreateProducerWithSchema(program,
            ["Identity"], schemaIdentity, identities);
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
